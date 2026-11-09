using System.IO;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>The snapshot timeline: loading it, and which entry is the target and which the baseline.</summary>
public sealed partial class MainViewModel
{
    private SnapshotItemViewModel? _selectedSnapshot;
    private SnapshotItemViewModel? _baselineSnapshot;
    private string _storageLabel = string.Empty;

    /// <summary>The snapshot being examined (or the live scan).</summary>
    public SnapshotItemViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (!SetProperty(ref _selectedSnapshot, value))
            {
                return;
            }

            UpdateTimelineFlags();
            OnPropertyChanged(nameof(CompareDescription));
            RefreshCommands();

            if (!_suspendRecompute)
            {
                AutoPickBaseline();
                _ = RecomputeDiffAsync();
            }
        }
    }

    /// <summary>What the target is compared against. Defaults to the snapshot just before the target.</summary>
    public SnapshotItemViewModel? BaselineSnapshot
    {
        get => _baselineSnapshot;
        set
        {
            if (!SetProperty(ref _baselineSnapshot, value))
            {
                return;
            }

            UpdateTimelineFlags();
            OnPropertyChanged(nameof(CompareDescription));
            RefreshCommands();

            if (!_suspendRecompute)
            {
                _ = RecomputeDiffAsync();
            }
        }
    }

    public bool HasSnapshots => Snapshots.Count > 0;

    public bool HasSavedSnapshots => Snapshots.Any(s => !s.IsLive);

    public bool IsLiveComparison => _selectedSnapshot?.IsLive == true;

    public string StorageLabel
    {
        get => _storageLabel;
        private set => SetProperty(ref _storageLabel, value);
    }

    /// <summary>Header describing what is being compared.</summary>
    public string CompareDescription
    {
        get
        {
            if (_selectedSnapshot is null)
            {
                return "No snapshot selected";
            }

            if (_baselineSnapshot is null)
            {
                return "First snapshot — there is nothing earlier to compare with";
            }

            return $"{_baselineSnapshot.TimeLabel}   →   {_selectedSnapshot.TimeLabel}";
        }
    }

    public string EmptyStateMessage
    {
        get
        {
            if (!HasFolder)
            {
                return "Choose a folder to watch, then take a snapshot to remember its current state.";
            }

            return HasSnapshots
                ? string.Empty
                : "No snapshots for this folder yet. Press \"Take snapshot\" to save its current state.";
        }
    }

    /// <summary>
    /// Loads the timeline for the current folder. <paramref name="includeLive"/> prepends the
    /// unsaved live scan so it can serve as the target.
    /// </summary>
    private async Task LoadTimelineAsync(string? selectSnapshotId, bool includeLive)
    {
        if (!HasFolder)
        {
            _suspendRecompute = true;
            Snapshots.Clear();
            SelectedSnapshot = null;
            BaselineSnapshot = null;
            _suspendRecompute = false;

            UpdateTimelineFlags();
            ApplyDiff(null);
            OnPropertyChanged(nameof(HasSnapshots));
            OnPropertyChanged(nameof(HasSavedSnapshots));
            OnPropertyChanged(nameof(EmptyStateMessage));
            StorageLabel = string.Empty;
            return;
        }

        try
        {
            var folder = FolderPath.Trim();
            var project = _store.LoadOrCreateProject(folder);
            var timeline = await Task.Run(() => _store.LoadTimeline(project.ProjectKey));

            _projectKey = project.ProjectKey;
            _diffEngine = _service.CreateDiffEngine(_projectKey);

            var live = includeLive && _liveManifest is not null
                ? SnapshotItemViewModel.CreateLive(_liveManifest)
                : null;

            _suspendRecompute = true;

            Snapshots.Clear();

            if (live is not null)
            {
                Snapshots.Add(live);
            }

            foreach (var summary in timeline.OrderByDescending(s => s.CreatedAtUtc))
            {
                Snapshots.Add(new SnapshotItemViewModel(summary));
            }

            var selected = live
                           ?? (selectSnapshotId is null
                               ? Snapshots.FirstOrDefault()
                               : Snapshots.FirstOrDefault(s => !s.IsLive && s.SnapshotId == selectSnapshotId))
                           ?? Snapshots.FirstOrDefault();

            SelectedSnapshot = selected;
            AutoPickBaseline();

            _suspendRecompute = false;

            UpdateTimelineFlags();
            OnPropertyChanged(nameof(HasSnapshots));
            OnPropertyChanged(nameof(HasSavedSnapshots));
            OnPropertyChanged(nameof(EmptyStateMessage));
            RefreshCommands();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            await RecomputeDiffAsync();
            await RefreshStorageAsync();
        }
    }

    /// <summary>With no explicit baseline, compare against the snapshot immediately before the target.</summary>
    private void AutoPickBaseline()
    {
        var index = _selectedSnapshot is null ? -1 : Snapshots.IndexOf(_selectedSnapshot);
        var candidate = index >= 0 && index + 1 < Snapshots.Count ? Snapshots[index + 1] : null;

        if (ReferenceEquals(candidate, _baselineSnapshot))
        {
            return;
        }

        _suspendRecompute = true;
        BaselineSnapshot = candidate;
        _suspendRecompute = false;
    }

    private void UpdateTimelineFlags()
    {
        foreach (var item in Snapshots)
        {
            item.IsTarget = ReferenceEquals(item, _selectedSnapshot);
            item.IsBaseline = ReferenceEquals(item, _baselineSnapshot);
        }
    }

    private async Task RefreshStorageAsync()
    {
        var key = _projectKey;
        if (key.Length == 0)
        {
            StorageLabel = string.Empty;
            return;
        }

        var bytes = await Task.Run(() => _store.GetStoredBytes(key));

        if (key != _projectKey)
        {
            return;
        }

        var snapshots = Snapshots.Count(s => !s.IsLive);
        StorageLabel = $"{PathUtil.FormatBytes(bytes)} stored · {snapshots} snapshot{(snapshots == 1 ? string.Empty : "s")}";
    }
}
