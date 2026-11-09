using System.Security.Cryptography;
using System.Text;
using FileDiff.Core.Models;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

public class SnapshotStoreTests
{
    [Fact]
    public void Project_key_is_stable_per_folder_and_differs_between_folders()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);

        var first = store.GetProjectKey(workspace.WatchRoot);
        var again = store.GetProjectKey(workspace.WatchRoot.ToUpperInvariant());
        var other = store.GetProjectKey(Path.Combine(workspace.WatchRoot, "sub"));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task Timeline_is_ordered_oldest_first_and_survives_a_reload()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var start = DateTimeOffset.UtcNow;

        await SaveAsync(store, workspace.WatchRoot, start, ("a.txt", "one"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(5), ("a.txt", "two"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(9), ("a.txt", "three"));

        var reloaded = new SnapshotStore(workspace.DataRoot);
        var timeline = reloaded.LoadTimeline(project.ProjectKey);

        Assert.Equal(3, timeline.Count);
        Assert.True(timeline[0].CreatedAtUtc < timeline[1].CreatedAtUtc);
        Assert.True(timeline[1].CreatedAtUtc < timeline[2].CreatedAtUtc);
        Assert.Equal(timeline[^1].SnapshotId, reloaded.GetLatestSnapshotId(project.ProjectKey));
    }

    [Fact]
    public async Task Snapshot_ids_stay_unique_when_created_in_the_same_second()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var stamp = DateTimeOffset.UtcNow;

        var first = await SaveAsync(store, workspace.WatchRoot, stamp, ("a.txt", "one"));
        var second = await SaveAsync(store, workspace.WatchRoot, stamp, ("a.txt", "two"));

        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
        Assert.Equal(2, store.GetSnapshotCount(project.ProjectKey));
    }

    [Fact]
    public async Task A_manifest_round_trips_with_all_metadata()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);

        var saved = await SaveAsync(
            store,
            workspace.WatchRoot,
            DateTimeOffset.UtcNow,
            ("src/a.txt", "alpha"),
            ("b.txt", "beta"));

        var loaded = store.LoadManifest(project.ProjectKey, saved.SnapshotId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FileCount);
        Assert.Equal(saved.TotalBytes, loaded.TotalBytes);
        Assert.All(loaded.Files, file => Assert.True(file.HasBlob));
        Assert.Equal(
            "alpha",
            Encoding.UTF8.GetString(store.GetBlobStore(project.ProjectKey).TryRead(loaded.Index["src/a.txt"].Hash)!));
    }

    [Fact]
    public async Task Identical_content_is_shared_between_snapshots_and_written_once()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var start = DateTimeOffset.UtcNow;

        var first = await SaveAsync(store, workspace.WatchRoot, start, ("a.txt", "shared"));
        var second = await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(1), ("a.txt", "shared"));

        Assert.True(first.BlobBytes > 0);
        Assert.Equal(0, second.BlobBytes);
        Assert.Equal(1, store.GetBlobStore(project.ProjectKey).CountBlobs());
    }

    [Fact]
    public void Saving_the_same_folder_twice_reuses_one_project()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);

        var first = store.LoadOrCreateProject(workspace.WatchRoot);
        var second = store.LoadOrCreateProject(workspace.WatchRoot + Path.DirectorySeparatorChar);

        Assert.Equal(first.ProjectKey, second.ProjectKey);
        Assert.Equal(first.RootPath, second.RootPath);
    }

    [Fact]
    public async Task Retention_keeps_the_newest_snapshots_and_reclaims_orphaned_content()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var start = DateTimeOffset.UtcNow;

        await SaveAsync(store, workspace.WatchRoot, start, ("a.txt", "only-in-the-oldest"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(1), ("a.txt", "kept"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(2), ("a.txt", "kept"));

        Assert.Equal(2, store.GetBlobStore(project.ProjectKey).CountBlobs());

        var result = await store.ApplyRetentionAsync(project.ProjectKey, maxKeep: 2);

        Assert.Equal(1, result.DeletedSnapshots);
        Assert.True(result.FreedBytes > 0);
        Assert.Equal(2, store.GetSnapshotCount(project.ProjectKey));

        var blobs = store.GetBlobStore(project.ProjectKey);
        Assert.Equal(1, blobs.CountBlobs());
        Assert.NotNull(blobs.TryRead(ContentHash("kept")));
        Assert.Null(blobs.TryRead(ContentHash("only-in-the-oldest")));
    }

    [Fact]
    public async Task Retention_is_a_no_op_below_the_limit()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);

        await SaveAsync(store, workspace.WatchRoot, DateTimeOffset.UtcNow, ("a.txt", "one"));

        var result = await store.ApplyRetentionAsync(project.ProjectKey, maxKeep: 5);

        Assert.Equal(0, result.DeletedSnapshots);
        Assert.Equal(1, store.GetSnapshotCount(project.ProjectKey));
    }

    [Fact]
    public async Task Deleting_a_snapshot_removes_it_from_the_timeline()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var start = DateTimeOffset.UtcNow;

        var first = await SaveAsync(store, workspace.WatchRoot, start, ("a.txt", "one"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(1), ("a.txt", "two"));

        Assert.True(await store.DeleteSnapshotAsync(project.ProjectKey, first.SnapshotId));
        Assert.False(await store.DeleteSnapshotAsync(project.ProjectKey, first.SnapshotId));
        Assert.Equal(1, store.GetSnapshotCount(project.ProjectKey));
        Assert.Null(store.LoadManifest(project.ProjectKey, first.SnapshotId));
    }

    [Fact]
    public async Task Garbage_collection_keeps_shared_content_alive()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var start = DateTimeOffset.UtcNow;

        await SaveAsync(store, workspace.WatchRoot, start, ("a.txt", "shared"), ("b.txt", "unique-to-first"));
        await SaveAsync(store, workspace.WatchRoot, start.AddMinutes(1), ("a.txt", "shared"));

        var deleted = await store.DeleteSnapshotAsync(
            project.ProjectKey,
            store.LoadTimeline(project.ProjectKey)[0].SnapshotId);

        Assert.True(deleted);

        var gc = store.CollectGarbage(project.ProjectKey);

        Assert.Equal(1, gc.DeletedBlobs);
        Assert.NotNull(store.GetBlobStore(project.ProjectKey).TryRead(ContentHash("shared")));
    }

    [Fact]
    public void Unknown_properties_in_stored_files_are_ignored()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);

        // An index written by a future version of the app must still load.
        var indexPath = store.GetIndexPath(project.ProjectKey);
        Directory.CreateDirectory(store.GetSnapshotDirectory(project.ProjectKey, "20260101-000000-000"));

        File.WriteAllText(
            Path.Combine(
                store.GetSnapshotDirectory(project.ProjectKey, "20260101-000000-000"),
                "manifest.json"),
            """
            {
              "schemaVersion": "9.9",
              "snapshotId": "20260101-000000-000",
              "rootPath": "C:\\watch",
              "createdAtUtc": "2026-01-01T00:00:00+00:00",
              "fileCount": 1,
              "totalBytes": 5,
              "somethingFromTheFuture": [1, 2, 3],
              "files": [
                {
                  "relativePath": "a.txt",
                  "size": 5,
                  "lastWriteTimeUtc": "2026-01-01T00:00:00+00:00",
                  "hash": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                  "isText": true,
                  "hasBlob": true,
                  "futureField": "ignored"
                }
              ]
            }
            """);

        File.WriteAllText(
            indexPath,
            """
            [ { "snapshotId": "20260101-000000-000", "rootPath": "C:\\watch", "createdAtUtc": "2026-01-01T00:00:00+00:00",
                "fileCount": 1, "totalBytes": 5, "unknownStat": 42 } ]
            """);

        var timeline = store.LoadTimeline(project.ProjectKey);
        var loaded = store.LoadManifest(project.ProjectKey, "20260101-000000-000");

        Assert.Single(timeline);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.FileCount);
        Assert.Equal("a.txt", loaded.Index["a.txt"].RelativePath);
        Assert.True(loaded.Index["a.txt"].IsText);
    }

    [Fact]
    public void A_corrupt_index_is_quarantined_instead_of_throwing()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);
        var project = store.LoadOrCreateProject(workspace.WatchRoot);

        File.WriteAllText(store.GetIndexPath(project.ProjectKey), "{ this is not json");

        Assert.Empty(store.LoadTimeline(project.ProjectKey));
        Assert.True(File.Exists(store.GetIndexPath(project.ProjectKey) + ".corrupt"));
    }

    [Fact]
    public async Task Stored_bytes_reflects_what_is_on_disk()
    {
        using var workspace = new TempWorkspace();
        var store = new SnapshotStore(workspace.DataRoot);

        var project = store.LoadOrCreateProject(workspace.WatchRoot);
        var beforeAnySnapshot = store.GetStoredBytes(project.ProjectKey);

        Assert.Equal(0, store.GetSnapshotCount(project.ProjectKey));
        Assert.True(beforeAnySnapshot >= 0);

        await SaveAsync(store, workspace.WatchRoot, DateTimeOffset.UtcNow, ("a.txt", "some content"));

        Assert.True(store.GetStoredBytes(project.ProjectKey) > beforeAnySnapshot);
    }

    private static string ContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static async Task<ScanManifest> SaveAsync(
        SnapshotStore store,
        string root,
        DateTimeOffset at,
        params (string Path, string Content)[] files)
    {
        var project = store.LoadOrCreateProject(root);
        var blobs = store.GetBlobStore(project.ProjectKey);

        var manifest = new ScanManifest
        {
            RootPath = project.RootPath,
            CreatedAtUtc = at,
            SnapshotId = store.CreateSnapshotId(project.ProjectKey, at),
            MachineName = "test",
            ContentCaptured = true,
        };

        foreach (var (relativePath, content) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = ContentHash(content);
            var written = blobs.Write(hash, bytes);

            manifest.Files.Add(new FileEntry
            {
                RelativePath = relativePath,
                Size = bytes.Length,
                LastWriteTimeUtc = at,
                Hash = hash,
                IsText = true,
                HasBlob = true,
            });

            manifest.BlobCount++;
            manifest.BlobBytes += written;
        }

        manifest.FileCount = manifest.Files.Count;
        manifest.TotalBytes = manifest.Files.Sum(f => f.Size);

        await store.SaveSnapshotAsync(project.ProjectKey, manifest);
        return manifest;
    }
}
