namespace FileDiff.Core.Diffing;

public enum ChangeKind
{
    Added,
    Removed,
    Modified,
    Moved,
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

/// <summary>One file that differs between two snapshots.</summary>
public sealed class FileChange
{
    public ChangeKind Kind { get; init; }

    /// <summary>New path for Added/Modified/Moved; the old path for Removed.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Previous path when the file was moved or renamed.</summary>
    public string? OldPath { get; init; }

    public long OldSize { get; init; }

    public long NewSize { get; init; }

    public string? OldHash { get; init; }

    public string? NewHash { get; init; }

    public DateTimeOffset? OldModifiedUtc { get; init; }

    public DateTimeOffset? NewModifiedUtc { get; init; }

    /// <summary>Added line count, or null when it has not been computed.</summary>
    public int? AddedLines { get; set; }

    /// <summary>Removed line count, or null when it has not been computed.</summary>
    public int? RemovedLines { get; set; }

    public long SizeDelta => NewSize - OldSize;

    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "Added",
        ChangeKind.Removed => "Deleted",
        ChangeKind.Modified => "Modified",
        _ => "Moved",
    };

    public string DisplayPath => Kind == ChangeKind.Removed ? Path : Path;
}

/// <summary>A single line of a rendered diff.</summary>
public sealed class DiffLine
{
    public DiffLineKind Kind { get; init; }

    public string Text { get; init; } = string.Empty;

    public int? OldLineNumber { get; init; }

    public int? NewLineNumber { get; init; }

    /// <summary>For paired add/remove lines: the unchanged leading part.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>For paired add/remove lines: the part that actually changed.</summary>
    public string Emphasis { get; set; } = string.Empty;

    /// <summary>For paired add/remove lines: the unchanged trailing part.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>True when the line was split into prefix/emphasis/suffix for inline highlighting.</summary>
    public bool HasEmphasis { get; set; }

    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        _ => " ",
    };
}

/// <summary>A contiguous block of changes plus its surrounding context lines.</summary>
public sealed class DiffHunk
{
    public int OldStart { get; init; }

    public int OldCount { get; init; }

    public int NewStart { get; init; }

    public int NewCount { get; init; }

    public List<DiffLine> Lines { get; init; } = [];

    public string Header => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@";
}

/// <summary>Result of comparing two snapshots at a high level.</summary>
public sealed class DiffResult
{
    public string? BaselineId { get; init; }

    public string? TargetId { get; init; }

    public DateTimeOffset? BaselineTimeUtc { get; init; }

    public DateTimeOffset? TargetTimeUtc { get; init; }

    public string RootPath { get; init; } = string.Empty;

    public List<FileChange> Changes { get; init; } = [];

    public int UnchangedCount { get; init; }

    /// <summary>Files whose timestamp/size moved but whose content is byte-for-byte identical.</summary>
    public int MetadataOnlyCount { get; init; }

    public int BaselineFileCount { get; init; }

    public int TargetFileCount { get; init; }

    public int AddedCount => Changes.Count(c => c.Kind == ChangeKind.Added);

    public int RemovedCount => Changes.Count(c => c.Kind == ChangeKind.Removed);

    public int ModifiedCount => Changes.Count(c => c.Kind == ChangeKind.Modified);

    public int MovedCount => Changes.Count(c => c.Kind == ChangeKind.Moved);

    public int AddedLines => Changes.Sum(c => c.AddedLines ?? 0);

    public int RemovedLines => Changes.Sum(c => c.RemovedLines ?? 0);

    public long AddedBytes => Changes.Where(c => c.Kind is ChangeKind.Added).Sum(c => c.NewSize);

    public long RemovedBytes => Changes.Where(c => c.Kind is ChangeKind.Removed).Sum(c => c.OldSize);

    public bool HasChanges => Changes.Count > 0;

    public string Summary() => Changes.Count == 0
        ? "No differences — the folder is identical to the baseline."
        : $"{Changes.Count} changed file{(Changes.Count == 1 ? string.Empty : "s")}: "
          + $"{AddedCount} added, {ModifiedCount} modified, {RemovedCount} deleted, {MovedCount} moved";
}

/// <summary>Content diff for one file, ready to render or export.</summary>
public sealed class FileDiffDocument
{
    public required FileChange Change { get; init; }

    public string Path => Change.Path;

    public bool IsBinary { get; set; }

    public bool TooLarge { get; set; }

    public string? UnavailableReason { get; set; }

    public string? OldEncoding { get; set; }

    public string? NewEncoding { get; set; }

    public string? OldLineEnding { get; set; }

    public string? NewLineEnding { get; set; }

    public int OldLineCount { get; set; }

    public int NewLineCount { get; set; }

    /// <summary>False when the previous content did not end with a line break (drives "\ No newline at end of file").</summary>
    public bool OldEndsWithNewline { get; set; } = true;

    /// <summary>False when the new content does not end with a line break.</summary>
    public bool NewEndsWithNewline { get; set; } = true;

    public int AddedLines { get; set; }

    public int RemovedLines { get; set; }

    public List<DiffHunk> Hunks { get; set; } = [];

    public bool HasContent => UnavailableReason is null && !IsBinary && !TooLarge;

    /// <summary>Notable but non-blocking observations, e.g. a line-ending change.</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>Output of the line differ.</summary>
public sealed class LineDiffResult
{
    public List<DiffHunk> Hunks { get; init; } = [];

    public int AddedLines { get; init; }

    public int RemovedLines { get; init; }

    public bool TooLarge { get; init; }

    public static LineDiffResult CreateTooLarge() => new() { TooLarge = true };
}
