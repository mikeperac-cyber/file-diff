namespace FileDiff.Core.Models;

/// <summary>
/// One file as observed by a scan. This is what gets persisted inside a snapshot manifest.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Path relative to the scanned root, forward-slash normalized.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>Last write time as reported by the file system.</summary>
    public DateTimeOffset LastWriteTimeUtc { get; set; }

    /// <summary>Lower-case SHA-256 of the file contents. Identical content always means identical hash.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>True when the content looked like decodable text.</summary>
    public bool IsText { get; set; }

    /// <summary>True when the (compressed) content was captured into the content-addressed blob store.</summary>
    public bool HasBlob { get; set; }

    public FileEntry Clone() => new()
    {
        RelativePath = RelativePath,
        Size = Size,
        LastWriteTimeUtc = LastWriteTimeUtc,
        Hash = Hash,
        IsText = IsText,
        HasBlob = HasBlob,
    };
}
