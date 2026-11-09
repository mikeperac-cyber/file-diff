using FileDiff.Core.Models;
using FileDiff.Core.Scanning;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

/// <summary>
/// Covers the "state of the files" machinery that makes repeated snapshots fast and compact.
/// </summary>
public class ScannerIncrementalTests
{
    private static BlobStore NewBlobStore(TempWorkspace workspace) =>
        new(Path.Combine(workspace.DataRoot, "blobs"));

    [Fact]
    public async Task Identical_content_is_stored_only_once_across_files()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("a.txt", "same content");
        workspace.Write("nested/b.txt", "same content");
        workspace.Write("c.txt", "different content");

        var store = NewBlobStore(workspace);
        var manifest = await new Scanner(store).ScanAsync(workspace.NewScanOptions(), null);

        Assert.Equal(3, manifest.FileCount);
        Assert.Equal(3, manifest.BlobCount);
        Assert.Equal(2, store.CountBlobs());
    }

    [Fact]
    public async Task Unchanged_files_keep_their_hash_without_being_read_again()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write("a.txt", "AAAA");
        var options = workspace.NewScanOptions();
        var store = NewBlobStore(workspace);

        var first = await new Scanner(store).ScanAsync(options, null);
        var originalStamp = File.GetLastWriteTimeUtc(path);

        // Same length, different content, timestamp restored. The size+timestamp fast path is
        // allowed to skip the re-read, which is what makes rescanning a large folder cheap.
        File.WriteAllText(path, "BBBB");
        File.SetLastWriteTimeUtc(path, originalStamp);
        Assert.Equal(originalStamp, File.GetLastWriteTimeUtc(path));

        var second = await new Scanner(store).ScanAsync(options, first);

        Assert.Equal(first.Files[0].Hash, second.Files[0].Hash);
    }

    [Fact]
    public async Task Same_size_change_with_a_new_timestamp_is_detected()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write("a.txt", "AAAA");
        var options = workspace.NewScanOptions();
        var store = NewBlobStore(workspace);

        var first = await new Scanner(store).ScanAsync(options, null);

        File.WriteAllText(path, "BBBB");
        File.SetLastWriteTimeUtc(path, first.Files[0].LastWriteTimeUtc.UtcDateTime.AddMinutes(1));

        var second = await new Scanner(store).ScanAsync(options, first);

        Assert.NotEqual(first.Files[0].Hash, second.Files[0].Hash);
        Assert.Equal("BBBB", File.ReadAllText(path));
    }

    [Fact]
    public async Task Length_change_is_detected_even_without_a_timestamp_change()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Write("a.txt", "AAAA");
        var options = workspace.NewScanOptions();
        var store = NewBlobStore(workspace);

        var first = await new Scanner(store).ScanAsync(options, null);

        File.WriteAllText(path, "AAAAAAAAAA");
        File.SetLastWriteTimeUtc(path, first.Files[0].LastWriteTimeUtc.UtcDateTime);

        var second = await new Scanner(store).ScanAsync(options, first);

        Assert.NotEqual(first.Files[0].Hash, second.Files[0].Hash);
        Assert.Equal(10, second.Files[0].Size);
    }

    [Fact]
    public async Task Unchanged_files_produce_no_new_blob_writes_on_rescan()
    {
        using var workspace = new TempWorkspace();
        for (var i = 0; i < 5; i++)
        {
            workspace.Write($"file{i}.txt", $"content {i}");
        }

        var store = NewBlobStore(workspace);
        var options = workspace.NewScanOptions();

        var first = await new Scanner(store).ScanAsync(options, null);
        var second = await new Scanner(store).ScanAsync(options, first);

        Assert.Equal(5, first.BlobCount);
        Assert.True(first.BlobBytes > 0);

        // Nothing changed, so nothing was re-read and nothing new was written.
        Assert.Equal(0, second.BlobBytes);
        Assert.Equal(5, store.CountBlobs());
    }

    [Fact]
    public async Task Deleted_files_simply_do_not_appear_in_the_next_scan()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("keep.txt", "keep");
        workspace.Write("gone.txt", "gone");

        var store = NewBlobStore(workspace);
        var options = workspace.NewScanOptions();

        var first = await new Scanner(store).ScanAsync(options, null);
        workspace.Delete("gone.txt");
        var second = await new Scanner(store).ScanAsync(options, first);

        Assert.Equal(2, first.FileCount);
        Assert.Equal(["keep.txt"], second.Files.Select(f => f.RelativePath).ToArray());
    }

    [Fact]
    public async Task Empty_files_are_tracked_without_a_blob()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("empty.txt", string.Empty);

        var store = NewBlobStore(workspace);
        var manifest = await new Scanner(store).ScanAsync(workspace.NewScanOptions(), null);

        Assert.Equal(0, manifest.Files[0].Size);
        Assert.False(manifest.Files[0].HasBlob);

        // The SHA-256 of zero bytes is a real, stable hash.
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", manifest.Files[0].Hash);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("a.txt", "a");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new Scanner(NewBlobStore(workspace))
                .ScanAsync(workspace.NewScanOptions(), null, null, cancellation.Token));
    }

    [Fact]
    public async Task Progress_reports_both_the_walk_and_the_finish()
    {
        using var workspace = new TempWorkspace();
        for (var i = 0; i < 40; i++)
        {
            workspace.Write($"f{i}.txt", new string('a', i + 1));
        }

        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        var manifest = await new Scanner(NewBlobStore(workspace)).ScanAsync(workspace.NewScanOptions(), null, progress);

        Assert.Equal(40, manifest.FileCount);
        Assert.Contains(reports, r => r.Phase == ScanPhase.Enumerating);
        Assert.Contains(reports, r => r.Phase == ScanPhase.Completed);
    }
}
