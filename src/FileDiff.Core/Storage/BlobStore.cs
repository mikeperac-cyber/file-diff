using System.IO.Compression;
using FileDiff.Core.Util;

namespace FileDiff.Core.Storage;

/// <summary>
/// Content-addressed store for file bytes.
/// <para>
/// A blob's name is the hash of its content, so the same content is only ever stored once —
/// across every snapshot of a project. That is what makes keeping many snapshots cheap, and
/// it makes renames free to detect. Contents are Brotli-compressed, which shrinks text
/// noticeably better than gzip.
/// </para>
/// </summary>
public sealed class BlobStore
{
    private const string Extension = ".br";

    /// <summary>Upper bound on what a single blob may decompress to; guards against pathological input.</summary>
    private const long DefaultReadCap = 256L * 1024 * 1024;

    private readonly string _root;

    public BlobStore(string blobsRoot) => _root = blobsRoot;

    public string Root => _root;

    public static bool IsValidHash(string? hash) =>
        !string.IsNullOrEmpty(hash)
        && hash.Length >= 4
        && hash.All(Uri.IsHexDigit);

    public string GetBlobPath(string hash)
    {
        var safe = hash.ToLowerInvariant();
        return Path.Combine(_root, safe[..2], safe.Substring(2, 2), safe + Extension);
    }

    public bool Contains(string? hash) =>
        IsValidHash(hash) && File.Exists(GetBlobPath(hash!));

    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="hash"/>.
    /// Returns the number of compressed bytes newly written, or 0 when the blob already existed.
    /// </summary>
    public long Write(string hash, ReadOnlySpan<byte> content)
    {
        if (!IsValidHash(hash))
        {
            throw new ArgumentException($"Refusing to store blob with invalid hash '{hash}'.", nameof(hash));
        }

        var destination = GetBlobPath(hash);
        if (File.Exists(destination))
        {
            return 0;
        }

        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $"{hash}.{Guid.NewGuid():N}.tmp");

        try
        {
            long written;
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                using (var brotli = new BrotliStream(file, CompressionLevel.Optimal, leaveOpen: true))
                {
                    brotli.Write(content);
                }

                written = file.Length;
            }

            try
            {
                File.Move(temp, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                // Another writer beat us to it. Content-addressed, so identical by definition.
                SafeDelete(temp);
                return 0;
            }

            return written;
        }
        catch
        {
            SafeDelete(temp);
            throw;
        }
    }

    /// <summary>Decompressed bytes, or null when the blob is absent or unreadable.</summary>
    public byte[]? TryRead(string? hash, long maxBytes = DefaultReadCap)
    {
        if (!Contains(hash))
        {
            return null;
        }

        try
        {
            using var file = File.OpenRead(GetBlobPath(hash!));
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
            using var buffer = new MemoryStream();

            var chunk = new byte[64 * 1024];
            int read;
            while ((read = brotli.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Total compressed size of the pool.</summary>
    public long GetStoredBytes() => Directory.Exists(_root)
        ? Directory.EnumerateFiles(_root, "*" + Extension, SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length)
        : 0;

    public int CountBlobs() => Directory.Exists(_root)
        ? Directory.EnumerateFiles(_root, "*" + Extension, SearchOption.AllDirectories).Count()
        : 0;

    /// <summary>Deletes blobs no longer referenced by any kept snapshot.</summary>
    /// <returns>Freed bytes and deleted blob count from a single directory pass.</returns>
    public (long FreedBytes, int DeletedCount) DeleteUnreferenced(IReadOnlySet<string> referencedHashes)
    {
        if (!Directory.Exists(_root))
        {
            return (0, 0);
        }

        long freed = 0;
        var deleted = 0;

        foreach (var path in Directory.EnumerateFiles(_root, "*" + Extension, SearchOption.AllDirectories))
        {
            var hash = Path.GetFileNameWithoutExtension(path);
            if (referencedHashes.Contains(hash))
            {
                continue;
            }

            try
            {
                var length = new FileInfo(path).Length;
                File.Delete(path);
                freed += length;
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        PruneEmptyDirectories(_root);
        return (freed, deleted);
    }

    private static void PruneEmptyDirectories(string root)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
