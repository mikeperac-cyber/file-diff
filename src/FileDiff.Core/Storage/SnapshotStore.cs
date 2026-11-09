using System.Security.Cryptography;
using System.Text;
using FileDiff.Core.Models;
using FileDiff.Core.Util;

namespace FileDiff.Core.Storage;

public sealed record RetentionResult(int DeletedSnapshots, long FreedBytes);

public sealed record GarbageResult(int DeletedBlobs, long FreedBytes);

/// <summary>
/// Owns everything the app remembers about a folder:
/// <code>
/// &lt;AppData&gt;/FileDiff/projects/&lt;projectKey&gt;/
///     project.json                  folder metadata
///     index.json                    snapshot timeline (oldest first)
///     blobs/aa/bb/&lt;sha256&gt;.br       shared, de-duplicated content pool
///     snapshots/&lt;snapshotId&gt;/manifest.json
/// </code>
/// The project key is a hash of the folder path, so pointing the app at the same folder
/// again continues the same timeline instead of starting over.
/// </summary>
public sealed class SnapshotStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _projectsRoot;

    public SnapshotStore(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? AppPaths.DataRoot : Path.GetFullPath(dataRoot);
        _projectsRoot = Path.Combine(DataRoot, "projects");
        Directory.CreateDirectory(_projectsRoot);
    }

    public string DataRoot { get; }

    public string GetProjectKey(string rootPath)
    {
        var normalized = PathUtil.NormalizeRoot(rootPath).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public string GetProjectDirectory(string projectKey) => Path.Combine(_projectsRoot, projectKey);

    public string GetBlobsDirectory(string projectKey) => Path.Combine(GetProjectDirectory(projectKey), "blobs");

    public string GetSnapshotsDirectory(string projectKey) => Path.Combine(GetProjectDirectory(projectKey), "snapshots");

    public string GetSnapshotDirectory(string projectKey, string snapshotId) =>
        Path.Combine(GetSnapshotsDirectory(projectKey), snapshotId);

    public string GetIndexPath(string projectKey) => Path.Combine(GetProjectDirectory(projectKey), "index.json");

    public string GetProjectInfoPath(string projectKey) => Path.Combine(GetProjectDirectory(projectKey), "project.json");

    public BlobStore GetBlobStore(string projectKey) => new(GetBlobsDirectory(projectKey));

    /// <summary>Folder name shown to the user, e.g. "MyProject".</summary>
    public static string DescribeProject(ProjectInfo project)
    {
        var leaf = Path.GetFileName(project.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(leaf) ? project.RootPath : leaf;
    }

    public ProjectInfo LoadOrCreateProject(string rootPath)
    {
        var normalized = PathUtil.NormalizeRoot(rootPath);
        var key = GetProjectKey(normalized);
        var path = GetProjectInfoPath(key);

        var project = JsonFile.TryRead<ProjectInfo>(path);
        if (project is null)
        {
            var leaf = Path.GetFileName(normalized);
            project = new ProjectInfo
            {
                ProjectKey = key,
                RootPath = normalized,
                DisplayName = string.IsNullOrEmpty(leaf) ? normalized : leaf,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            JsonFile.WriteAtomic(path, project);
        }
        else if (!string.Equals(project.RootPath, normalized, StringComparison.OrdinalIgnoreCase))
        {
            project.RootPath = normalized;
            JsonFile.WriteAtomic(path, project);
        }

        return project;
    }

    public void SaveProject(ProjectInfo project) =>
        JsonFile.WriteAtomic(GetProjectInfoPath(project.ProjectKey), project);

    /// <summary>Timeline ordered oldest first, which is the order retention prunes in.</summary>
    public IReadOnlyList<SnapshotSummary> LoadTimeline(string projectKey) =>
        JsonFile.TryRead<List<SnapshotSummary>>(GetIndexPath(projectKey)) ?? [];

    public ScanManifest? LoadManifest(string projectKey, string snapshotId)
    {
        var path = Path.Combine(GetSnapshotDirectory(projectKey, snapshotId), "manifest.json");
        var manifest = JsonFile.TryRead<ScanManifest>(path);

        if (manifest is not null)
        {
            manifest.SnapshotDirectory = GetSnapshotDirectory(projectKey, snapshotId);
        }

        return manifest;
    }

    /// <summary>Id that sorts chronologically and never collides inside the timeline.</summary>
    public string CreateSnapshotId(string projectKey, DateTimeOffset timestampUtc)
    {
        var timeline = LoadTimeline(projectKey);
        var existing = timeline.Select(s => s.SnapshotId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = ScanManifest.BuildSnapshotId(timestampUtc);

        if (!existing.Contains(candidate) && !Directory.Exists(GetSnapshotDirectory(projectKey, candidate)))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var withSuffix = $"{candidate}-{suffix}";
            if (!existing.Contains(withSuffix) && !Directory.Exists(GetSnapshotDirectory(projectKey, withSuffix)))
            {
                return withSuffix;
            }
        }

        return $"{ScanManifest.BuildSnapshotId(timestampUtc)}-{Guid.NewGuid():N}"[..34];
    }

    /// <summary>Persists a manifest and appends it to the timeline.</summary>
    public async Task SaveSnapshotAsync(string projectKey, ScanManifest manifest, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var snapshotDirectory = GetSnapshotDirectory(projectKey, manifest.SnapshotId);
            Directory.CreateDirectory(snapshotDirectory);
            manifest.SnapshotDirectory = snapshotDirectory;
            JsonFile.WriteAtomic(Path.Combine(snapshotDirectory, "manifest.json"), manifest);

            var timeline = LoadTimeline(projectKey).ToList();
            timeline.RemoveAll(s => string.Equals(s.SnapshotId, manifest.SnapshotId, StringComparison.OrdinalIgnoreCase));
            timeline.Add(ToSummary(manifest));
            timeline.Sort((a, b) => a.CreatedAtUtc.CompareTo(b.CreatedAtUtc));
            JsonFile.WriteAtomic(GetIndexPath(projectKey), timeline);

            var project = LoadOrCreateProject(manifest.RootPath);
            project.SnapshotCount = timeline.Count;
            project.LastScannedAtUtc = manifest.CreatedAtUtc;
            SaveProject(project);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteSnapshotAsync(string projectKey, string snapshotId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var timeline = LoadTimeline(projectKey).ToList();
            var removed = timeline.RemoveAll(
                s => string.Equals(s.SnapshotId, snapshotId, StringComparison.OrdinalIgnoreCase)) > 0;

            if (!removed)
            {
                return false;
            }

            JsonFile.WriteAtomic(GetIndexPath(projectKey), timeline);
            TryDeleteDirectory(GetSnapshotDirectory(projectKey, snapshotId));

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Keeps the newest <paramref name="maxKeep"/> snapshots and reclaims the disk no longer referenced.</summary>
    public async Task<RetentionResult> ApplyRetentionAsync(string projectKey, int maxKeep, CancellationToken cancellationToken = default)
    {
        if (maxKeep <= 0)
        {
            return new RetentionResult(0, 0);
        }

        var deleted = 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var timeline = LoadTimeline(projectKey).ToList();
            if (timeline.Count > maxKeep)
            {
                var dropCount = timeline.Count - maxKeep;

                for (var i = 0; i < dropCount; i++)
                {
                    TryDeleteDirectory(GetSnapshotDirectory(projectKey, timeline[i].SnapshotId));
                    deleted++;
                }

                timeline.RemoveRange(0, dropCount);
                JsonFile.WriteAtomic(GetIndexPath(projectKey), timeline);
            }
        }
        finally
        {
            _gate.Release();
        }

        var gc = CollectGarbage(projectKey);
        return new RetentionResult(deleted, gc.FreedBytes);
    }

    /// <summary>Removes blobs that no remaining snapshot refers to.</summary>
    public GarbageResult CollectGarbage(string projectKey)
    {
        // OrdinalIgnoreCase set: no per-file ToLowerInvariant allocation.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in LoadTimeline(projectKey))
        {
            var manifest = LoadManifest(projectKey, summary.SnapshotId);
            if (manifest is null)
            {
                continue;
            }

            foreach (var file in manifest.Files)
            {
                if (file.HasBlob && BlobStore.IsValidHash(file.Hash))
                {
                    referenced.Add(file.Hash);
                }
            }
        }

        var store = GetBlobStore(projectKey);
        var (freed, deleted) = store.DeleteUnreferenced(referenced);

        return new GarbageResult(deleted, freed);
    }

    /// <summary>Compressed bytes currently held for a project (index + manifests + blobs).</summary>
    public long GetStoredBytes(string projectKey)
    {
        var directory = GetProjectDirectory(projectKey);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return total;
    }

    public int GetSnapshotCount(string projectKey) => LoadTimeline(projectKey).Count;

    public string? GetLatestSnapshotId(string projectKey)
    {
        var timeline = LoadTimeline(projectKey);
        return timeline.Count == 0 ? null : timeline[^1].SnapshotId;
    }

    public static SnapshotSummary ToSummary(ScanManifest manifest) => new()
    {
        SnapshotId = manifest.SnapshotId,
        RootPath = manifest.RootPath,
        CreatedAtUtc = manifest.CreatedAtUtc,
        FileCount = manifest.FileCount,
        TotalBytes = manifest.TotalBytes,
        BlobCount = manifest.BlobCount,
        BlobBytes = manifest.BlobBytes,
        DurationSeconds = manifest.DurationSeconds,
        WarningCount = manifest.Warnings.Count,
        ContentCaptured = manifest.ContentCaptured,
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
