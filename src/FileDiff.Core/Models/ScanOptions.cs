using FileDiff.Core.Scanning;

namespace FileDiff.Core.Models;

/// <summary>Everything that controls how a folder is walked and what gets captured.</summary>
public sealed class ScanOptions
{
    /// <summary>Folder to snapshot. Must exist.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Content larger than this is hashed but its bytes are not stored, so no content diff is offered.</summary>
    public long MaxTextFileBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>When false, hidden/system entries are skipped entirely.</summary>
    public bool IncludeHiddenFiles { get; set; }

    /// <summary>When false, only hashes/sizes are recorded and no content diff is possible.</summary>
    public bool CaptureContent { get; set; } = true;

    /// <summary>Extra globs on top of <see cref="IgnoreRules.DefaultPatterns"/>.</summary>
    public List<string> IgnorePatterns { get; set; } = [];

    /// <summary>How many snapshots to keep for the project before the oldest are pruned.</summary>
    public int MaxSnapshotsToKeep { get; set; } = 50;

    /// <summary>Effective ignore matcher: defaults plus any user-supplied patterns.</summary>
    public GlobMatcher BuildMatcher() => new(IgnoreRules.Combine(IgnorePatterns));

    public ScanOptions Clone() => new()
    {
        RootPath = RootPath,
        MaxTextFileBytes = MaxTextFileBytes,
        IncludeHiddenFiles = IncludeHiddenFiles,
        CaptureContent = CaptureContent,
        IgnorePatterns = [.. IgnorePatterns],
        MaxSnapshotsToKeep = MaxSnapshotsToKeep,
    };
}
