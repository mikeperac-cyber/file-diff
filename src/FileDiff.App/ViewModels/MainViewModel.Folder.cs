using System.IO;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Models;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>Folder selection, recent folders, and the busy/progress state shown in the side panel.</summary>
public sealed partial class MainViewModel
{
    private string _folderPath = string.Empty;
    private string? _selectedRecentFolder;
    private bool _isBusy;
    private bool _isComputingDiff;
    private string _statusText = "Ready";
    private double _progressValue;
    private bool _progressIndeterminate;

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (!SetProperty(ref _folderPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FolderName));
            OnPropertyChanged(nameof(CanCapture));
            OnPropertyChanged(nameof(EmptyStateMessage));
            RefreshCommands();
        }
    }

    public string FolderName
    {
        get
        {
            var trimmed = _folderPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                return "no folder selected";
            }

            var leaf = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
        }
    }

    /// <summary>Picking a recent folder fills the path box; the selection then clears so it can be picked again.</summary>
    public string? SelectedRecentFolder
    {
        get => _selectedRecentFolder;
        set
        {
            if (!SetProperty(ref _selectedRecentFolder, value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                FolderPath = value;
                _ = LoadTimelineAsync(null, includeLive: false);
                _selectedRecentFolder = null;
                OnPropertyChanged(nameof(SelectedRecentFolder));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanCapture));
                RefreshCommands();
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public bool IsComputingDiff
    {
        get => _isComputingDiff;
        private set => SetProperty(ref _isComputingDiff, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => SetProperty(ref _progressIndeterminate, value);
    }

    public bool HasFolder =>
        !string.IsNullOrWhiteSpace(_folderPath) && Directory.Exists(_folderPath.Trim());

    public bool CanCapture => !_isBusy && HasFolder;

    private void BrowseForFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder you want to watch",
            Multiselect = false,
        };

        if (HasFolder)
        {
            dialog.InitialDirectory = _folderPath.Trim();
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FolderPath = dialog.FolderName;
        _ = LoadTimelineAsync(null, includeLive: false);
    }

    private void SyncRecentFolders()
    {
        RecentFolders.Clear();

        foreach (var path in _settings.RecentRootPaths)
        {
            RecentFolders.Add(path);
        }

        OnPropertyChanged(nameof(HasRecentFolders));
    }

    public bool HasRecentFolders => RecentFolders.Count > 0;

    /// <summary>Shown in the status bar, e.g. "FileDiff 1.0.0".</summary>
    public string VersionLabel { get; } =
        "FileDiff " + (typeof(ObservableObject).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    private void OnScanProgress(ScanProgress progress)
    {
        StatusText = progress.Describe();
        var fraction = progress.Fraction;
        ProgressIndeterminate = fraction is null;
        ProgressValue = (fraction ?? 0) * 100;
    }

    private void Cancel()
    {
        try
        {
            _cancellation?.Cancel();
            StatusText = "Cancelling…";
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
