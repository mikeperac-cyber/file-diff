using System.Text.Json.Serialization;

namespace FileDiff.Core.Models;

/// <summary>
/// The persisted result of one scan. This is the "state of the files" the app remembers,
/// and it is the unit that two diffs are computed between.
/// </summary>
public sealed class ScanManifest
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Sortable unique id, e.g. "20260925-143012-481".</summary>
    public string SnapshotId { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    /// <summary>Number of distinct contents captured for this snapshot (after de-duplication).</summary>
    public int BlobCount { get; set; }

    /// <summary>Compressed bytes contributed to the shared blob pool by this snapshot.</summary>
    public long BlobBytes { get; set; }

    public double DurationSeconds { get; set; }

    public bool ContentCaptured { get; set; } = true;

    public List<FileEntry> Files { get; set; } = [];

    /// <summary>Non-fatal problems (locked files, unreadable folders) collected during the walk.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Snapshot of the settings used, so old snapshots stay explainable.</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string SnapshotDirectory { get; set; } = string.Empty;

    private Dictionary<string, FileEntry>? _index;

    /// <summary>Case-insensitive lookup by relative path, built lazily.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, FileEntry> Index
    {
        get
        {
            var cached = _index;
            if (cached is not null)
            {
                return cached;
            }

            var built = new Dictionary<string, FileEntry>(Files.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var file in Files)
            {
                built[file.RelativePath] = file;
            }

            _index = built;
            return built;
        }
    }

    public static string BuildSnapshotId(DateTimeOffset timestampUtc)
    {
        var id = timestampUtc.ToString("yyyyMMdd-HHmmss");
        var millis = timestampUtc.Millisecond.ToString("000");
        return $"{id}-{millis}";
    }

    public ScanManifest CloneWithoutFiles() => new()
    {
        SchemaVersion = SchemaVersion,
        SnapshotId = SnapshotId,
        RootPath = RootPath,
        CreatedAtUtc = CreatedAtUtc,
        MachineName = MachineName,
        AppVersion = AppVersion,
        FileCount = FileCount,
        TotalBytes = TotalBytes,
        BlobCount = BlobCount,
        BlobBytes = BlobBytes,
        DurationSeconds = DurationSeconds,
        ContentCaptured = ContentCaptured,
        SnapshotDirectory = SnapshotDirectory,
        Warnings = [.. Warnings],
        Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase),
    };
}
