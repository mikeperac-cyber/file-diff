using FileDiff.App.Infrastructure;
using FileDiff.Core.Models;
using FileDiff.Core.Storage;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>One entry in the snapshot timeline.</summary>
public sealed class SnapshotItemViewModel : ObservableObject
{
    private bool _isBaseline;
    private bool _isTarget;

    public SnapshotItemViewModel(SnapshotSummary summary, bool isLive = false)
    {
        Summary = summary;
        IsLive = isLive;
    }

    /// <summary>An unsaved "what changed right now?" scan shown alongside real snapshots.</summary>
    public static SnapshotItemViewModel CreateLive(ScanManifest manifest) =>
        new(SnapshotStore.ToSummary(manifest), isLive: true);

    public SnapshotSummary Summary { get; }

    public bool IsLive { get; }

    public string SnapshotId => Summary.SnapshotId;

    public DateTimeOffset CreatedAtUtc => Summary.CreatedAtUtc;

    public bool IsBaseline
    {
        get => _isBaseline;
        set => SetProperty(ref _isBaseline, value);
    }

    public bool IsTarget
    {
        get => _isTarget;
        set => SetProperty(ref _isTarget, value);
    }

    public string TimeLabel => IsLive
        ? "Live scan — not saved"
        : Summary.LocalTime.ToString("ddd d MMM yyyy, HH:mm:ss");

    public string AgeLabel
    {
        get
        {
            if (IsLive)
            {
                return "scanned just now";
            }

            var elapsed = DateTimeOffset.UtcNow - CreatedAtUtc;

            return elapsed switch
            {
                { TotalSeconds: < 90 } => "just now",
                { TotalMinutes: < 90 } => $"{(int)elapsed.TotalMinutes} min ago",
                { TotalHours: < 36 } => $"{(int)elapsed.TotalHours} h ago",
                { TotalDays: < 8 } => $"{(int)elapsed.TotalDays} d ago",
                _ => CreatedAtUtc.ToLocalTime().ToString("d MMM yyyy"),
            };
        }
    }

    public string FileCountLabel => $"{Summary.FileCount:N0} files";

    public string SizeLabel => PathUtil.FormatBytes(Summary.TotalBytes);

    public string DetailLabel
    {
        get
        {
            var parts = new List<string>(3);

            if (!IsLive)
            {
                parts.Add($"{Summary.DurationSeconds:0.0}s");
            }

            parts.Add(PathUtil.FormatBytes(Summary.BlobBytes) + " captured");

            if (Summary.WarningCount > 0)
            {
                parts.Add(Summary.WarningCount == 1 ? "1 warning" : $"{Summary.WarningCount} warnings");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasWarnings => Summary.WarningCount > 0;

    public string Tooltip =>
        $"Snapshot {SnapshotId}{Environment.NewLine}"
        + $"Taken {Summary.LocalTime:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
        + $"{Summary.FileCount:N0} files, {PathUtil.FormatBytes(Summary.TotalBytes)} in total{Environment.NewLine}"
        + $"{Summary.BlobCount:N0} captured contents{Environment.NewLine}"
        + $"Stored size is de-duplicated across the whole timeline";
}
