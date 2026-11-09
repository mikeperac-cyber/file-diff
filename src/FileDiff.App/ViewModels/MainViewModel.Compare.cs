using FileDiff.App.Infrastructure;
using FileDiff.Core.Diffing;

namespace FileDiff.App.ViewModels;

/// <summary>Computing the comparison between the baseline and the target snapshot.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Recomputes the comparison. A newer request silently supersedes an older one.</summary>
    private async Task RecomputeDiffAsync()
    {
        var generation = ++_diffGeneration;
        var engine = _diffEngine;
        var projectKey = _projectKey;
        var targetId = _selectedSnapshot?.IsLive == true ? LiveMarker : _selectedSnapshot?.SnapshotId;
        var baselineId = _baselineSnapshot?.IsLive == true ? LiveMarker : _baselineSnapshot?.SnapshotId;
        var live = _liveManifest;
        var store = _store;

        if (engine is null || projectKey.Length == 0 || targetId is null || baselineId is null)
        {
            ApplyDiff(null);
            return;
        }

        var options = new DiffOptions
        {
            ContextLines = ContextLines,
            ComputeLineStats = true,
            LineStatsLimit = LineStatsLimit,
        };

        IsComputingDiff = true;

        try
        {
            var result = await Task.Run(() =>
            {
                var target = targetId == LiveMarker ? live : store.LoadManifest(projectKey, targetId);
                var baseline = baselineId == LiveMarker ? live : store.LoadManifest(projectKey, baselineId);

                return target is null || baseline is null
                    ? null
                    : engine.Compare(baseline, target, options);
            });

            if (generation != _diffGeneration)
            {
                return;
            }

            ApplyDiff(result);
        }
        catch (Exception exception)
        {
            if (generation == _diffGeneration)
            {
                ReportError(exception);
            }
        }
        finally
        {
            if (generation == _diffGeneration)
            {
                IsComputingDiff = false;
            }
        }
    }

    private void ApplyDiff(DiffResult? diff)
    {
        _currentDiff = diff;
        _allChanges = diff is null
            ? []
            : [.. diff.Changes.Select(change => new FileChangeViewModel(change))];

        _hasDiff = diff is not null;
        _addedCount = diff?.AddedCount ?? 0;
        _removedCount = diff?.RemovedCount ?? 0;
        _modifiedCount = diff?.ModifiedCount ?? 0;
        _movedCount = diff?.MovedCount ?? 0;
        _unchangedCount = diff?.UnchangedCount ?? 0;

        _compareHeader = diff is null
            ? CompareDescription
            : $"{DescribeBaseline()}   →   {DescribeTarget()}";

        _summaryLine = diff switch
        {
            null => "Pick a folder and take a snapshot — the next snapshot will show exactly what changed.",
            { HasChanges: false } => BuildNoChangeSummary(diff),
            _ => diff.Summary() + BuildMetadataSuffix(diff),
        };

        ApplyFilter();

        OnPropertyChanged(nameof(CompareHeader));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(HasDiff));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(RemovedCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(MovedCount));
        OnPropertyChanged(nameof(UnchangedCount));
        OnPropertyChanged(nameof(HasNoChanges));
        OnPropertyChanged(nameof(HasComparableDiff));
        OnPropertyChanged(nameof(IsLiveComparison));
        RefreshCommands();
    }

    private static string BuildNoChangeSummary(DiffResult diff) =>
        diff.UnchangedCount == 0
            ? "No changes — both snapshots describe an identical folder."
            : $"No changes — all {diff.UnchangedCount:N0} files are byte-for-byte identical to the baseline."
              + BuildMetadataSuffix(diff);

    private static string BuildMetadataSuffix(DiffResult diff) =>
        diff.MetadataOnlyCount > 0
            ? $" ({diff.MetadataOnlyCount:N0} file(s) were rewritten with identical content)"
            : string.Empty;

    private string DescribeBaseline() => _baselineSnapshot?.TimeLabel ?? "the earliest scan";

    private string DescribeTarget() => _selectedSnapshot?.IsLive == true
        ? "live scan (not saved)"
        : _selectedSnapshot?.TimeLabel ?? "the latest snapshot";
}
