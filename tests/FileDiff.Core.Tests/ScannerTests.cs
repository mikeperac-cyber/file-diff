using System.Security.Cryptography;
using System.Text;
using FileDiff.Core.Models;
using FileDiff.Core.Scanning;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

public class ScannerTests
{
    private static BlobStore NewBlobStore(TempWorkspace workspace) =>
        new(Path.Combine(workspace.DataRoot, "blobs"));

    [Fact]
    public async Task Discovers_nested_files_with_normalized_relative_paths()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("readme.md", "# hi");
        workspace.Write("src/deep/nested/Program.cs", "class P {}");

        var manifest = await new Scanner(NewBlobStore(workspace)).ScanAsync(workspace.NewScanOptions(), null);

        Assert.Equal(2, manifest.FileCount);
        Assert.Equal(
            ["readme.md", "src/deep/nested/Program.cs"],
            manifest.Files.Select(f => f.RelativePath).ToArray());
        Assert.Equal(workspace.WatchRoot, manifest.RootPath);
        Assert.Empty(manifest.Warnings);
    }

    [Fact]
    public async Task Hash_is_the_sha256_of_the_content()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("a.txt", "hello");

        var manifest = await new Scanner(NewBlobStore(workspace)).ScanAsync(workspace.NewScanOptions(), null);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello"))).ToLowerInvariant();

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", expected);
        Assert.Equal(expected, manifest.Files[0].Hash);
        Assert.True(manifest.Files[0].IsText);
        Assert.True(manifest.Files[0].HasBlob);
    }

    [Fact]
    public async Task Default_ignores_and_user_patterns_are_applied()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("keep.js", "let a = 1;");
        workspace.Write("node_modules/react/index.js", "module.exports = {};");
        workspace.Write(".git/config", "[core]");
        workspace.Write("noise.log", "log line");
        workspace.Write("custom/skipme.txt", "skip");

        var options = workspace.NewScanOptions();
        options.IgnorePatterns.Add("custom/");

        var manifest = await new Scanner(NewBlobStore(workspace)).ScanAsync(options, null);

        Assert.Equal(["keep.js"], manifest.Files.Select(f => f.RelativePath).ToArray());
    }

    [Fact]
    public async Task Binary_files_are_classified_and_not_captured()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteBytes("image.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02]);
        workspace.WriteBytes("mystery.dat", [0x00, 0x01, 0x02, 0x03]);

        var manifest = await new Scanner(NewBlobStore(workspace)).ScanAsync(workspace.NewScanOptions(), null);

        foreach (var file in manifest.Files)
        {
            Assert.False(file.IsText, file.RelativePath);
            Assert.False(file.HasBlob, file.RelativePath);
            Assert.NotEmpty(file.Hash);
        }
    }

    [Fact]
    public async Task Files_over_the_capture_limit_are_hashed_but_not_stored()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("big.txt", new string('x', 5000));

        var options = workspace.NewScanOptions();
        options.MaxTextFileBytes = 100;

        var store = NewBlobStore(workspace);
        var manifest = await new Scanner(store).ScanAsync(options, null);

        Assert.NotEmpty(manifest.Files[0].Hash);
        Assert.True(manifest.Files[0].IsText);
        Assert.False(manifest.Files[0].HasBlob);
        Assert.Equal(0, store.CountBlobs());
    }

    [Fact]
    public async Task Content_capture_can_be_switched_off_entirely()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("a.txt", "content");

        var options = workspace.NewScanOptions();
        options.CaptureContent = false;

        var store = NewBlobStore(workspace);
        var manifest = await new Scanner(store).ScanAsync(options, null);

        Assert.False(manifest.Files[0].HasBlob);
        Assert.False(manifest.ContentCaptured);
        Assert.Equal(0, store.CountBlobs());
    }

    [Fact]
    public async Task Hidden_files_are_skipped_unless_requested()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("visible.txt", "v");
        var hidden = workspace.Write("hidden.txt", "h");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var excluded = await new Scanner(NewBlobStore(workspace)).ScanAsync(workspace.NewScanOptions(), null);

        var includingOptions = workspace.NewScanOptions();
        includingOptions.IncludeHiddenFiles = true;
        var included = await new Scanner(NewBlobStore(workspace)).ScanAsync(includingOptions, null);

        Assert.Equal(["visible.txt"], excluded.Files.Select(f => f.RelativePath).ToArray());
        Assert.Equal(2, included.FileCount);
    }

    [Fact]
    public async Task Missing_folder_is_reported_clearly()
    {
        using var workspace = new TempWorkspace();
        var options = new ScanOptions { RootPath = Path.Combine(workspace.WatchRoot, "nope") };

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new Scanner(NewBlobStore(workspace)).ScanAsync(options, null));
    }
}
