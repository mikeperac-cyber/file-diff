using System.Text.Json.Serialization;

namespace FileDiff.Core.Models;

/// <summary>Lightweight timeline entry; avoids reading every manifest just to paint the sidebar.</summary>
public sealed class SnapshotSummary
{
    public string SnapshotId { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    public int BlobCount { get; set; }

    public long BlobBytes { get; set; }

    public double DurationSeconds { get; set; }

    public int WarningCount { get; set; }

    public bool ContentCaptured { get; set; } = true;

    /// <summary>Display convenience only — never persisted, because the index stores UTC.</summary>
    [JsonIgnore]
    public DateTimeOffset LocalTime => CreatedAtUtc.ToLocalTime();
}
