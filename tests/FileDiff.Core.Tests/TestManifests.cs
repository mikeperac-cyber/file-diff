using System.Security.Cryptography;
using System.Text;
using FileDiff.Core.Models;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

/// <summary>Builds snapshot manifests by hand so diff behaviour can be tested in isolation.</summary>
internal static class TestManifests
{
    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    public static ScanManifest Build(
        BlobStore? blobs,
        DateTimeOffset createdAt,
        params (string Path, string Content)[] files)
    {
        var manifest = new ScanManifest
        {
            SnapshotId = createdAt.ToString("yyyyMMdd-HHmmss-fff"),
            RootPath = @"C:\watch",
            CreatedAtUtc = createdAt,
            MachineName = "test",
            ContentCaptured = blobs is not null,
        };

        foreach (var (path, content) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = Hash(content);

            manifest.Files.Add(new FileEntry
            {
                RelativePath = path,
                Size = bytes.Length,
                LastWriteTimeUtc = createdAt,
                Hash = hash,
                IsText = true,
                HasBlob = blobs is not null && blobs.Write(hash, bytes) >= 0,
            });
        }

        manifest.FileCount = manifest.Files.Count;
        manifest.TotalBytes = manifest.Files.Sum(f => f.Size);
        return manifest;
    }
}
