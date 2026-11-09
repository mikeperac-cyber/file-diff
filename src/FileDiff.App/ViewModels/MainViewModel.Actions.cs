using System.Diagnostics;
using System.IO;
using System.Windows;
using FileDiff.Core.Models;
using FileDiff.Core.Storage;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>User actions: taking snapshots, managing the timeline, opening things, cleaning up.</summary>
public sealed partial class MainViewModel
{
    private async Task CaptureAsync(bool save)
    {
        var folder = FolderPath.Trim();

        if (!Directory.Exists(folder))
        {
            StatusText = $"That folder no longer exists: {folder}";
            return;
        }

        _settings.RememberRoot(folder);
        _settings.Save();
        SyncRecentFolders();

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        StatusText = save ? "Reading the folder…" : "Checking what changed…";

        try
        {
            var progress = new Progress<ScanProgress>(OnScanProgress);
            var capture = await _service.CaptureAsync(BuildScanOptions(folder), save, progress, _cancellation.Token);

            _projectKey = capture.Project.ProjectKey;
            _diffEngine = _service.CreateDiffEngine(_projectKey);
            _liveManifest = save ? null : capture.Manifest;

            await LoadTimelineAsync(save ? capture.Manifest.SnapshotId : null, includeLive: !save);

            var notes = new List<string>();

            if (capture.Retention is { DeletedSnapshots: > 0 })
            {
                notes.Add($"{capture.Retention.DeletedSnapshots} older snapshot(s) pruned");
            }

            if (capture.Manifest.Warnings.Count > 0)
            {
                notes.Add($"{capture.Manifest.Warnings.Count} item(s) skipped");
                AppLog.Warn(
                    $"Scan of {folder} finished with {capture.Manifest.Warnings.Count} warning(s): "
                    + string.Join(" | ", capture.Manifest.Warnings.Take(5)));
            }

            StatusText = (save ? $"Snapshot {capture.Manifest.SnapshotId} saved" : "Live scan finished")
                + $" · {capture.Manifest.FileCount:N0} files"
                + $" · {PathUtil.FormatBytes(capture.Manifest.TotalBytes)}"
                + $" · {capture.Manifest.DurationSeconds:0.0}s"
                + (notes.Count > 0 ? $" · {string.Join(", ", notes)}" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            IsBusy = false;
            ProgressIndeterminate = false;
            ProgressValue = 0;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private ScanOptions BuildScanOptions(string folder) => new()
    {
        RootPath = folder,
        CaptureContent = CaptureContent,
        IncludeHiddenFiles = IncludeHiddenFiles,
        IgnorePatterns = [.. ParseIgnorePatterns()],
        MaxSnapshotsToKeep = _settings.MaxSnapshotsToKeep,
        MaxTextFileBytes = MaxContentMegabytes * 1024L * 1024L,
    };

    private async Task DeleteSelectedSnapshotAsync()
    {
        var selected = _selectedSnapshot;

        if (selected is null || selected.IsLive || _projectKey.Length == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Delete snapshot {selected.SnapshotId}?\n\n"
            + "The remembered file states for that point in time are removed, and any stored content "
            + "that only it used is reclaimed.",
            "Delete snapshot",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var key = _projectKey;
            var id = selected.SnapshotId;

            await _store.DeleteSnapshotAsync(key, id);
            var collected = await Task.Run(() => _store.CollectGarbage(key));

            StatusText = $"Snapshot {id} deleted · {PathUtil.FormatBytes(collected.FreedBytes)} reclaimed.";
            await LoadTimelineAsync(null, includeLive: false);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetSelectedAsBaseline()
    {
        if (_selectedSnapshot is null)
        {
            return;
        }

        _suspendRecompute = true;
        BaselineSnapshot = _selectedSnapshot;
        _suspendRecompute = false;

        StatusText = "Baseline changed — comparing against "
                     + (_selectedSnapshot.IsLive ? "the live scan" : _selectedSnapshot.SnapshotId) + ".";
        _ = RecomputeDiffAsync();
    }

    private void CompareWithLatestPrevious()
    {
        var latest = Snapshots.FirstOrDefault(s => !s.IsLive);

        if (latest is null)
        {
            return;
        }

        var index = Snapshots.IndexOf(latest);

        _suspendRecompute = true;
        SelectedSnapshot = latest;
        BaselineSnapshot = index + 1 < Snapshots.Count ? Snapshots[index + 1] : null;
        _suspendRecompute = false;

        _ = RecomputeDiffAsync();
    }

    private void SwapComparison()
    {
        var target = _selectedSnapshot;
        var baseline = _baselineSnapshot;

        if (target is null || baseline is null)
        {
            return;
        }

        _suspendRecompute = true;

        SelectedSnapshot = Snapshots.FirstOrDefault(s => ReferenceEquals(s, baseline)) ?? baseline;
        BaselineSnapshot = Snapshots.FirstOrDefault(s => ReferenceEquals(s, target)) ?? target;

        _suspendRecompute = false;

        _ = RecomputeDiffAsync();
    }

    private void OpenSnapshotFolder()
    {
        if (_selectedSnapshot is null || _selectedSnapshot.IsLive || _projectKey.Length == 0)
        {
            return;
        }

        OpenPath(_store.GetSnapshotDirectory(_projectKey, _selectedSnapshot.SnapshotId));
    }

    private void OpenSelectedFileInExplorer()
    {
        var change = _selectedChange;

        if (change is null || !HasFolder)
        {
            return;
        }

        var root = FolderPath.Trim();
        var filePath = PathUtil.ToLocalPath(root, change.Path);

        if (File.Exists(filePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception exception)
            {
                ReportError(exception);
            }

            return;
        }

        // The file does not exist in this snapshot any more; open the folder it lived in.
        var parent = Path.GetDirectoryName(change.Path)?.Replace('\\', '/');
        OpenPath(parent is { Length: > 0 } ? PathUtil.ToLocalPath(root, parent) : root);
    }

    private void CopySelectedPath()
    {
        var change = _selectedChange;

        if (change is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(change.Path);
            StatusText = $"Copied path: {change.Path}";
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Clipboard unavailable: {exception.Message}");
        }
    }

    private async Task CleanupAsync()
    {
        if (_projectKey.Length == 0)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "Reclaiming unused stored content…";

            var key = _projectKey;
            var result = await Task.Run(() => _store.CollectGarbage(key));

            StatusText = result.DeletedBlobs == 0
                ? "Nothing to clean up — every stored content is still referenced."
                : $"Removed {result.DeletedBlobs} unused content file(s) · "
                  + $"{PathUtil.FormatBytes(result.FreedBytes)} reclaimed.";

            await RefreshStorageAsync();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenLogFolder() => OpenPath(AppPaths.DataRoot);

    private void OpenPath(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                StatusText = $"Nothing to open at {path}";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ReportError(Exception exception)
    {
        AppLog.Error("Operation failed", exception);
        StatusText = "Failed: " + exception.Message;

        MessageBox.Show(
            exception.Message,
            "FileDiff",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
