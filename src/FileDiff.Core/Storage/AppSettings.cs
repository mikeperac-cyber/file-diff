using FileDiff.Core.Models;

namespace FileDiff.Core.Storage;

/// <summary>User preferences, persisted to <c>%APPDATA%\FileDiff\settings.json</c>.</summary>
public sealed class AppSettings
{
    public List<string> IgnorePatterns { get; set; } = [];

    public int MaxSnapshotsToKeep { get; set; } = 50;

    public long MaxTextFileBytes { get; set; } = 5 * 1024 * 1024;

    public bool IncludeHiddenFiles { get; set; }

    public bool CaptureContent { get; set; } = true;

    /// <summary>When true, "Snapshot now" writes a snapshot; when false it only compares.</summary>
    public bool SaveSnapshotByDefault { get; set; } = true;

    public int ContextLines { get; set; } = 3;

    public string? LastRootPath { get; set; }

    public List<string> RecentRootPaths { get; set; } = [];

    /// <summary>Pinned commander favorites (Alt+1..9). Persisted.</summary>
    public List<string> FavoriteRootPaths { get; set; } = [];

    public string Theme { get; set; } = "Light";

    public string FilePath { get; set; } = AppPaths.SettingsFile;

    public static AppSettings Load(string? path = null)
    {
        var target = path ?? AppPaths.SettingsFile;
        var settings = JsonFile.TryRead<AppSettings>(target) ?? new AppSettings();
        settings.FilePath = target;
        settings.Normalize();
        return settings;
    }

    public void Save()
    {
        Normalize();
        JsonFile.WriteAtomic(FilePath, this);
    }

    public void RememberRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        var normalized = Util.PathUtil.NormalizeRoot(rootPath);
        LastRootPath = normalized;
        RecentRootPaths.RemoveAll(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        RecentRootPaths.Insert(0, normalized);

        while (RecentRootPaths.Count > 12)
        {
            RecentRootPaths.RemoveAt(RecentRootPaths.Count - 1);
        }
    }

    public ScanOptions ToScanOptions(string rootPath) => new()
    {
        RootPath = rootPath,
        CaptureContent = CaptureContent,
        IncludeHiddenFiles = IncludeHiddenFiles,
        IgnorePatterns = [.. IgnorePatterns],
        MaxSnapshotsToKeep = MaxSnapshotsToKeep,
        MaxTextFileBytes = MaxTextFileBytes,
    };

    private void Normalize()
    {
        IgnorePatterns ??= [];
        RecentRootPaths ??= [];
        FavoriteRootPaths ??= [];
        MaxSnapshotsToKeep = Math.Clamp(MaxSnapshotsToKeep, 1, 5000);
        MaxTextFileBytes = Math.Clamp(MaxTextFileBytes, 64 * 1024, 512L * 1024 * 1024);
        ContextLines = Math.Clamp(ContextLines, 0, 50);
        Theme = string.IsNullOrWhiteSpace(Theme) ? "Light" : Theme;
    }
}
