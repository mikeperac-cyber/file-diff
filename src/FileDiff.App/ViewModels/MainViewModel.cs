using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Diffing;
using FileDiff.Core.Models;
using FileDiff.Core.Scanning;
using FileDiff.Core.Storage;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>
/// Drives the whole window: taking snapshots, keeping the timeline, comparing any two states and
/// exposing the readable diff to the view.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string LiveMarker = "live";
    private const int LineStatsLimit = 300;

    private readonly AppSettings _settings;
    private readonly SnapshotStore _store;
    private readonly SnapshotService _service;
    private readonly List<ICommandState> _commands = [];

    private CancellationTokenSource? _cancellation;
    private DiffEngine? _diffEngine;
    private DiffResult? _currentDiff;
    private ScanManifest? _liveManifest;
    private List<FileChangeViewModel> _allChanges = [];
    private string _projectKey = string.Empty;
    private int _diffGeneration;
    private bool _suspendRecompute;
    private bool _suspendDocumentLoad;
    private bool _loadingSettings;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _store = new SnapshotStore();
        _service = new SnapshotService(_store);

        BrowseCommand = Track(new RelayCommand(BrowseForFolder));
        SnapshotNowCommand = Track(new AsyncRelayCommand(() => CaptureAsync(save: true), () => CanCapture));
        CheckChangesCommand = Track(new AsyncRelayCommand(
            () => CaptureAsync(save: false),
            () => CanCapture && Snapshots.Any(s => !s.IsLive)));
        CancelCommand = Track(new RelayCommand(Cancel, () => IsBusy));
        RefreshCommand = Track(new AsyncRelayCommand(
            () => LoadTimelineAsync(null, includeLive: false),
            () => !IsBusy && _projectKey.Length > 0));
        DeleteSnapshotCommand = Track(new AsyncRelayCommand(
            DeleteSelectedSnapshotAsync,
            () => SelectedSnapshot is { IsLive: false }));
        SetAsBaselineCommand = Track(new RelayCommand(
            SetSelectedAsBaseline,
            () => SelectedSnapshot is { IsLive: false } or { IsLive: true }));
        CompareWithPreviousCommand = Track(new RelayCommand(
            CompareWithLatestPrevious,
            () => Snapshots.Count(s => !s.IsLive) > 0));
        SwapComparisonCommand = Track(new RelayCommand(
            SwapComparison,
            () => BaselineSnapshot is not null && SelectedSnapshot is not null));
        OpenSnapshotFolderCommand = Track(new RelayCommand(
            OpenSnapshotFolder,
            () => SelectedSnapshot is { IsLive: false }));
        OpenInExplorerCommand = Track(new RelayCommand(OpenSelectedFileInExplorer, () => SelectedChange is not null));
        CopyPathCommand = Track(new RelayCommand(CopySelectedPath, () => SelectedChange is not null));
        ExportPatchCommand = Track(new AsyncRelayCommand(() => ExportAsync(ExportFormat.Patch), () => HasComparableDiff));
        ExportHtmlCommand = Track(new AsyncRelayCommand(() => ExportAsync(ExportFormat.Html), () => HasComparableDiff));
        ExportCsvCommand = Track(new AsyncRelayCommand(() => ExportAsync(ExportFormat.Csv), () => HasComparableDiff));
        CleanupCommand = Track(new AsyncRelayCommand(CleanupAsync, () => !IsBusy && _projectKey.Length > 0));
        OpenLogFolderCommand = Track(new RelayCommand(OpenLogFolder));
        ToggleSettingsCommand = Track(new RelayCommand(() => SettingsExpanded = !SettingsExpanded));

        RegisterCommanderCommands();
        RegisterThemeCommand();
        RegisterNotesCommands();
        LoadSettingsIntoUi();
        SyncRecentFolders();

        _ = InitializeAsync();
    }

    public ObservableCollection<SnapshotItemViewModel> Snapshots { get; } = [];

    public ObservableCollection<FileChangeViewModel> Changes { get; } = [];

    public ObservableCollection<DiffLineViewModel> DiffLines { get; } = [];

    public ObservableCollection<string> RecentFolders { get; } = [];

    public RelayCommand BrowseCommand { get; }

    public AsyncRelayCommand SnapshotNowCommand { get; }

    public AsyncRelayCommand CheckChangesCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand DeleteSnapshotCommand { get; }

    public RelayCommand SetAsBaselineCommand { get; }

    public RelayCommand CompareWithPreviousCommand { get; }

    public RelayCommand SwapComparisonCommand { get; }

    public RelayCommand OpenSnapshotFolderCommand { get; }

    public RelayCommand OpenInExplorerCommand { get; }

    public RelayCommand CopyPathCommand { get; }

    public AsyncRelayCommand ExportPatchCommand { get; }

    public AsyncRelayCommand ExportHtmlCommand { get; }

    public AsyncRelayCommand ExportCsvCommand { get; }

    public AsyncRelayCommand CleanupCommand { get; }

    public RelayCommand OpenLogFolderCommand { get; }

    public RelayCommand ToggleSettingsCommand { get; }

    private T Track<T>(T command)
        where T : ICommandState
    {
        _commands.Add(command);
        return command;
    }

    private void RefreshCommands()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private async Task InitializeAsync()
    {
        var remembered = _settings.LastRootPath;

        if (!string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered))
        {
            _suspendRecompute = true;
            FolderPath = remembered;
            _suspendRecompute = false;
            await LoadTimelineAsync(null, includeLive: false);
            StatusText = $"Loaded {Snapshots.Count} saved snapshot(s) for {Path.GetFileName(remembered)}. Take a snapshot to see what changed.";
        }
        else
        {
            StatusText = "Choose the folder you want to watch, then take a snapshot.";
        }
    }
}
