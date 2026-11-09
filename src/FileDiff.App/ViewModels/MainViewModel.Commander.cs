using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FileDiff.App.Controls;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Commander;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>
/// Orthodox-commander ops (F3..F8) operating on the live folder + selected change.
/// SecondPanelPath is the passive panel: F5/F6 default there, Ctrl+U swaps panels.
/// Favorites are pinned roots (Ctrl+D), separate from recent folders.
/// </summary>
public sealed partial class MainViewModel
{
    private string _secondPanelPath = string.Empty;
    private string? _selectedFavorite;
    private string _quickViewText = string.Empty;
    private bool _hasQuickView;

    public ObservableCollection<string> FavoriteRoots { get; } = [];

    public string SecondPanelPath
    {
        get => _secondPanelPath;
        set
        {
            if (SetProperty(ref _secondPanelPath, value ?? string.Empty))
            {
                RefreshCommands();
            }
        }
    }

    public string? SelectedFavorite
    {
        get => _selectedFavorite;
        set
        {
            if (!SetProperty(ref _selectedFavorite, value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                SecondPanelPath = FolderPath.Trim();
                FolderPath = value;
                _ = LoadTimelineAsync(null, includeLive: false);
                _selectedFavorite = null;
                OnPropertyChanged(nameof(SelectedFavorite));
            }
        }
    }

    public string QuickViewText
    {
        get => _quickViewText;
        private set
        {
            if (SetProperty(ref _quickViewText, value))
            {
                OnPropertyChanged(nameof(HasQuickView));
            }
        }
    }

    public bool HasQuickView
    {
        get => _hasQuickView;
        private set => SetProperty(ref _hasQuickView, value);
    }

    public RelayCommand OpenSelectedFileCommand { get; private set; } = null!;

    public RelayCommand QuickViewCommand { get; private set; } = null!;

    public RelayCommand CopySelectedCommand { get; private set; } = null!;

    public RelayCommand MoveSelectedCommand { get; private set; } = null!;

    public RelayCommand RenameSelectedCommand { get; private set; } = null!;

    public RelayCommand MkdirCommand { get; private set; } = null!;

    public RelayCommand DeleteSelectedCommand { get; private set; } = null!;

    public RelayCommand ToggleFavoriteCommand { get; private set; } = null!;

    public RelayCommand SwapPanelsCommand { get; private set; } = null!;

    public RelayCommand ClearQuickViewCommand { get; private set; } = null!;

    private void RegisterCommanderCommands()
    {
        OpenSelectedFileCommand = Track(new RelayCommand(OpenSelectedFile, () => SelectedChange is not null && HasFolder));
        QuickViewCommand = Track(new RelayCommand(QuickViewSelected, () => SelectedChange is not null && HasFolder));
        CopySelectedCommand = Track(new RelayCommand(CopySelectedToSecondPanel, () => SelectedChange is not null && HasFolder && !IsBusy));
        MoveSelectedCommand = Track(new RelayCommand(MoveSelectedToSecondPanel, () => SelectedChange is not null && HasFolder && !IsBusy));
        RenameSelectedCommand = Track(new RelayCommand(RenameSelected, () => SelectedChange is not null && HasFolder && !IsBusy));
        MkdirCommand = Track(new RelayCommand(MkdirInFolder, () => HasFolder && !IsBusy));
        DeleteSelectedCommand = Track(new RelayCommand(DeleteSelectedFromDisk, () => SelectedChange is not null && HasFolder && !IsBusy));
        ToggleFavoriteCommand = Track(new RelayCommand(ToggleFavorite, () => HasFolder));
        SwapPanelsCommand = Track(new RelayCommand(SwapPanels, () => HasFolder));
        ClearQuickViewCommand = Track(new RelayCommand(() => { QuickViewText = string.Empty; HasQuickView = false; }));
        SyncFavorites();
    }

    private CommanderService? GetCommander() =>
        HasFolder ? new CommanderService(FolderPath.Trim()) : null;

    private string SelectedRelativePath() => _selectedChange?.Path ?? string.Empty;

    private static Window? ActiveWindow() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    private void OpenSelectedFile()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        try
        {
            var full = svc.ResolveSource(rel);
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                StatusText = $"No longer on disk: {rel}";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = full, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void QuickViewSelected()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        var view = svc.QuickView(rel);
        if (!view.Exists)
        {
            QuickViewText = $"Not found: {rel}";
            HasQuickView = true;
            return;
        }

        var header = view.IsDirectory ? $"[dir] {rel}" : $"[{PathUtil.FormatBytes(view.Size)}] {rel}";
        QuickViewText = header + Environment.NewLine + new string('─', 40) + Environment.NewLine
            + (view.PreviewText.Length == 0 ? "(empty)" : view.PreviewText)
            + (view.Truncated ? Environment.NewLine + "… (truncated, F4 to open full file)" : string.Empty);
        HasQuickView = true;
    }

    private string DefaultCopyDestination(string fileName)
    {
        if (!string.IsNullOrWhiteSpace(_secondPanelPath) && Directory.Exists(_secondPanelPath.Trim()))
        {
            return Path.Combine(_secondPanelPath.Trim(), fileName);
        }

        var owner = ActiveWindow();
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Copy to (second panel)" };
        return dlg.ShowDialog(owner) == true ? Path.Combine(dlg.FolderName, fileName) : string.Empty;
    }

    private void CopySelectedToSecondPanel()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        var dest = DefaultCopyDestination(Path.GetFileName(rel.Replace('/', Path.DirectorySeparatorChar)));
        if (string.IsNullOrEmpty(dest))
        {
            return;
        }

        var result = svc.Copy(rel, dest);
        StatusText = result.Message;
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "FileDiff — Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveSelectedToSecondPanel()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        var dest = DefaultCopyDestination(Path.GetFileName(rel.Replace('/', Path.DirectorySeparatorChar)));
        if (string.IsNullOrEmpty(dest))
        {
            return;
        }

        var result = svc.Move(rel, dest);
        StatusText = result.Message;
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "FileDiff — Move", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenameSelected()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        var owner = ActiveWindow();
        if (owner is null)
        {
            return;
        }

        var current = Path.GetFileName(rel.Replace('/', Path.DirectorySeparatorChar));
        var next = PromptDialog.Ask(owner, "Rename", $"Rename {rel} to:", current);
        if (string.IsNullOrWhiteSpace(next) || string.Equals(next, current, StringComparison.Ordinal))
        {
            return;
        }

        var result = svc.Rename(rel, next);
        StatusText = result.Message;
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "FileDiff — Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MkdirInFolder()
    {
        var svc = GetCommander();
        if (svc is null)
        {
            return;
        }

        var owner = ActiveWindow();
        if (owner is null)
        {
            return;
        }

        var name = PromptDialog.Ask(owner, "New folder", "Folder name (relative to watched folder):", "NewFolder");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var result = svc.CreateDirectory(name);
        StatusText = result.Message;
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "FileDiff — New folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteSelectedFromDisk()
    {
        var svc = GetCommander();
        var rel = SelectedRelativePath();
        if (svc is null || string.IsNullOrEmpty(rel))
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Permanently delete from disk?\n\n{rel}\n\nThis is NOT the snapshot — the file is removed for real.",
            "FileDiff — Delete (F8)",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var result = svc.Delete(rel, recursive: false);
        if (!result.Ok && result.Message.Contains("not empty", StringComparison.OrdinalIgnoreCase))
        {
            var retry = MessageBox.Show(
                $"{rel} is a non-empty folder.\nDelete recursively?",
                "FileDiff — Delete folder",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (retry == MessageBoxResult.OK)
            {
                result = svc.Delete(rel, recursive: true);
            }
        }

        StatusText = result.Message;
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "FileDiff — Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleFavorite()
    {
        var folder = FolderPath.Trim();
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var list = _settings.FavoriteRootPaths;
        var existing = list.FirstOrDefault(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            list.Insert(0, folder);
            StatusText = $"Pinned favorite: {folder}";
        }
        else
        {
            list.Remove(existing);
            StatusText = $"Unpinned favorite: {folder}";
        }

        Persist();
        SyncFavorites();
    }

    private void SyncFavorites()
    {
        FavoriteRoots.Clear();
        foreach (var path in _settings.FavoriteRootPaths)
        {
            FavoriteRoots.Add(path);
        }

        OnPropertyChanged(nameof(FavoriteRoots));
    }

    private void SwapPanels()
    {
        if (string.IsNullOrWhiteSpace(_secondPanelPath))
        {
            SecondPanelPath = FolderPath.Trim();
            StatusText = "Second panel set. Type another folder in Folder to compare panels, Ctrl+U swaps.";
            return;
        }

        var current = FolderPath.Trim();
        FolderPath = _secondPanelPath.Trim();
        SecondPanelPath = current;
        _ = LoadTimelineAsync(null, includeLive: false);
    }
}
