using FileDiff.App.Infrastructure;
using FileDiff.Core.Diffing;
using FileDiff.Core.Models;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>One row in the changed-files list.</summary>
public sealed class FileChangeViewModel : ObservableObject
{
    public FileChangeViewModel(FileChange change) => Change = change;

    public FileChange Change { get; }

    public ChangeKind Kind => Change.Kind;

    public string Path => Change.Path;

    public string KindLabel => Change.KindLabel;

    public bool IsMoved => Change.Kind == ChangeKind.Moved;

    public string? OldPath => Change.Kind == ChangeKind.Moved ? Change.OldPath : null;

    public string MovedFromLabel => OldPath is null ? string.Empty : $"moved from {OldPath}";

    public string SizeLabel => Change.Kind switch
    {
        ChangeKind.Added => PathUtil.FormatBytes(Change.NewSize),
        ChangeKind.Removed => PathUtil.FormatBytes(Change.OldSize),
        _ => $"{PathUtil.FormatBytes(Change.OldSize)} → {PathUtil.FormatBytes(Change.NewSize)}",
    };

    public string TimestampLabel
    {
        get
        {
            var stamp = Change.NewModifiedUtc ?? Change.OldModifiedUtc;
            return stamp is null ? string.Empty : stamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public bool HasLineCounts => Change.AddedLines.HasValue || Change.RemovedLines.HasValue;

    public int AddedLines => Change.AddedLines ?? 0;

    public int RemovedLines => Change.RemovedLines ?? 0;

    public string LinesLabel => HasLineCounts ? $"+{AddedLines}  −{RemovedLines}" : "—";

    public string Tooltip =>
        $"{KindLabel}: {Path}{Environment.NewLine}"
        + $"size {SizeLabel}{Environment.NewLine}"
        + (string.IsNullOrEmpty(TimestampLabel) ? string.Empty : $"modified {TimestampLabel}")
        + (string.IsNullOrEmpty(TimestampLabel) ? string.Empty : Environment.NewLine)
        + string.Join(',', new[] { LinesLabel }).Replace("  ", " ");

    /// <summary>Called when line counts are computed on demand after the fact.</summary>
    public void RefreshLineCounts()
    {
        OnPropertyChanged(nameof(HasLineCounts));
        OnPropertyChanged(nameof(AddedLines));
        OnPropertyChanged(nameof(RemovedLines));
        OnPropertyChanged(nameof(LinesLabel));
        OnPropertyChanged(nameof(Tooltip));
    }
}
