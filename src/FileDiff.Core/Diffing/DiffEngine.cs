using FileDiff.Core.Models;
using FileDiff.Core.Scanning;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Diffing;

/// <summary>Knobs for comparing two snapshots.</summary>
public sealed class DiffOptions
{
    public int ContextLines { get; set; } = 3;

    /// <summary>Detect renames/moves by matching identical content.</summary>
    public bool DetectMoves { get; set; } = true;

    /// <summary>Pre-compute +/- line counts so the changed-file list can show them immediately.</summary>
    public bool ComputeLineStats { get; set; } = true;

    /// <summary>Above this many changed files, line counts are computed on demand instead.</summary>
    public int LineStatsLimit { get; set; } = 400;
}

/// <summary>
/// Compares two snapshots of the same folder.
/// <para>
/// Comparison is content-based, never timestamp-based: a file whose timestamp moved but whose
/// bytes are identical is reported as unchanged (and counted separately as "metadata only"),
/// which keeps the signal-to-noise ratio high on folders that are touched a lot.
/// </para>
/// </summary>
public sealed class DiffEngine
{
    private const int SniffLength = 8000;

    private readonly BlobStore? _blobs;

    /// <summary>Pass the project's blob store to enable content diffs; pass null for a structure-only compare.</summary>
    public DiffEngine(BlobStore? blobs) => _blobs = blobs;

    public DiffResult Compare(ScanManifest? baseline, ScanManifest? target, DiffOptions? options = null)
    {
        options ??= new DiffOptions();

        var baselineIndex = baseline?.Index;
        var targetIndex = target?.Index;
        var changes = new List<FileChange>();
        var added = new List<FileEntry>();
        var removed = new List<FileEntry>();
        var unchanged = 0;
        var metadataOnly = 0;

        if (baselineIndex is not null && targetIndex is not null)
        {
            foreach (var targetEntry in targetIndex.Values)
            {
                if (!baselineIndex.TryGetValue(targetEntry.RelativePath, out var baselineEntry))
                {
                    added.Add(targetEntry);
                    continue;
                }

                if (HashesEqual(baselineEntry, targetEntry))
                {
                    unchanged++;

                    if (baselineEntry.LastWriteTimeUtc != targetEntry.LastWriteTimeUtc
                        || baselineEntry.Size != targetEntry.Size)
                    {
                        metadataOnly++;
                    }
                }
                else
                {
                    changes.Add(CreateChange(ChangeKind.Modified, targetEntry.RelativePath, baselineEntry, targetEntry));
                }
            }

            foreach (var baselineEntry in baselineIndex.Values)
            {
                if (!targetIndex.ContainsKey(baselineEntry.RelativePath))
                {
                    removed.Add(baselineEntry);
                }
            }
        }
        else if (targetIndex is not null)
        {
            added.AddRange(targetIndex.Values);
        }
        else if (baselineIndex is not null)
        {
            removed.AddRange(baselineIndex.Values);
        }

        if (options.DetectMoves && added.Count > 0 && removed.Count > 0)
        {
            DetectMoves(added, removed, changes);
        }

        foreach (var entry in added)
        {
            changes.Add(CreateChange(ChangeKind.Added, entry.RelativePath, null, entry));
        }

        foreach (var entry in removed)
        {
            changes.Add(CreateChange(ChangeKind.Removed, entry.RelativePath, entry, null));
        }

        changes.Sort((left, right) =>
        {
            var byPath = string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
            return byPath != 0 ? byPath : left.Kind.CompareTo(right.Kind);
        });

        if (options.ComputeLineStats && changes.Count > 0 && changes.Count <= options.LineStatsLimit)
        {
            ComputeLineStats(changes, options.ContextLines);
        }

        return new DiffResult
        {
            BaselineId = baseline?.SnapshotId,
            TargetId = target?.SnapshotId,
            BaselineTimeUtc = baseline?.CreatedAtUtc,
            TargetTimeUtc = target?.CreatedAtUtc,
            RootPath = target?.RootPath ?? baseline?.RootPath ?? string.Empty,
            Changes = changes,
            UnchangedCount = unchanged,
            MetadataOnlyCount = metadataOnly,
            BaselineFileCount = baseline?.FileCount ?? 0,
            TargetFileCount = target?.FileCount ?? 0,
        };
    }

    private static bool HashesEqual(FileEntry left, FileEntry right) =>
        left.Hash.Length > 0
        && right.Hash.Length > 0
        && string.Equals(left.Hash, right.Hash, StringComparison.OrdinalIgnoreCase);

    private static FileChange CreateChange(ChangeKind kind, string path, FileEntry? oldEntry, FileEntry? newEntry) => new()
    {
        Kind = kind,
        Path = path,
        OldPath = kind == ChangeKind.Moved ? oldEntry?.RelativePath : null,
        OldSize = oldEntry?.Size ?? 0,
        NewSize = newEntry?.Size ?? 0,
        OldHash = string.IsNullOrEmpty(oldEntry?.Hash) ? null : oldEntry!.Hash,
        NewHash = string.IsNullOrEmpty(newEntry?.Hash) ? null : newEntry!.Hash,
        OldModifiedUtc = oldEntry?.LastWriteTimeUtc,
        NewModifiedUtc = newEntry?.LastWriteTimeUtc,
    };

    /// <summary>
    /// Pairs deletions with additions that have byte-identical content, so a rename is reported
    /// as one "Moved" row instead of a delete plus an add. Zero-length files are excluded because
    /// every empty file shares the same hash.
    /// </summary>
    private static void DetectMoves(List<FileEntry> added, List<FileEntry> removed, List<FileChange> changes)
    {
        var addedByHash = GroupByHash(added);
        var removedByHash = GroupByHash(removed);
        var matchedAdded = new HashSet<FileEntry>();
        var matchedRemoved = new HashSet<FileEntry>();

        foreach (var oldEntry in removed
                     .Where(e => e.Size > 0 && e.Hash.Length > 0)
                     .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!removedByHash.TryGetValue(oldEntry.Hash, out var oldQueue)
                || !addedByHash.TryGetValue(oldEntry.Hash, out var newQueue)
                || oldQueue.Count == 0
                || newQueue.Count == 0)
            {
                continue;
            }

            var newEntry = newQueue.Dequeue();
            oldQueue.Dequeue();
            matchedAdded.Add(newEntry);
            matchedRemoved.Add(oldEntry);

            changes.Add(CreateChange(ChangeKind.Moved, newEntry.RelativePath, oldEntry, newEntry));
        }

        if (matchedAdded.Count > 0)
        {
            added.RemoveAll(matchedAdded.Contains);
            removed.RemoveAll(matchedRemoved.Contains);
        }
    }

    private static Dictionary<string, Queue<FileEntry>> GroupByHash(List<FileEntry> entries)
    {
        var groups = new Dictionary<string, Queue<FileEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries
                     .Where(e => e.Size > 0 && e.Hash.Length > 0)
                     .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!groups.TryGetValue(entry.Hash, out var queue))
            {
                queue = new Queue<FileEntry>();
                groups[entry.Hash] = queue;
            }

            queue.Enqueue(entry);
        }

        return groups;
    }

    /// <summary>Builds the renderable content diff for one changed file.</summary>
    public FileDiffDocument BuildFileDiff(FileChange change, int contextLines = 3)
    {
        ArgumentNullException.ThrowIfNull(change);

        var document = new FileDiffDocument { Change = change };

        var needsOld = change.Kind is ChangeKind.Removed or ChangeKind.Modified or ChangeKind.Moved;
        var needsNew = change.Kind is ChangeKind.Added or ChangeKind.Modified or ChangeKind.Moved;

        var oldBytes = Array.Empty<byte>();
        var newBytes = Array.Empty<byte>();

        if (needsOld)
        {
            var loaded = _blobs?.TryRead(change.OldHash);
            if (loaded is null)
            {
                document.UnavailableReason = DescribeUnavailable(change, "previous");
                return document;
            }

            oldBytes = loaded;
        }

        if (needsNew)
        {
            var loaded = _blobs?.TryRead(change.NewHash);
            if (loaded is null)
            {
                document.UnavailableReason = DescribeUnavailable(change, "new");
                return document;
            }

            newBytes = loaded;
        }

        if (LooksBinary(oldBytes) || LooksBinary(newBytes))
        {
            document.IsBinary = true;
            document.UnavailableReason = "Binary file — only presence, size and timestamp are compared.";
            return document;
        }

        var oldText = TextDecoder.Decode(oldBytes);
        var newText = TextDecoder.Decode(newBytes);

        document.OldEncoding = oldText.EncodingName;
        document.NewEncoding = newText.EncodingName;
        document.OldLineEnding = oldText.LineEnding;
        document.NewLineEnding = newText.LineEnding;
        document.OldLineCount = oldText.Lines.Count;
        document.NewLineCount = newText.Lines.Count;
        document.OldEndsWithNewline = oldBytes.Length == 0 || EndsWithNewline(oldBytes);
        document.NewEndsWithNewline = newBytes.Length == 0 || EndsWithNewline(newBytes);

        if (needsOld && needsNew)
        {
            if (oldText.LineEnding is not null
                && newText.LineEnding is not null
                && !string.Equals(oldText.LineEnding, newText.LineEnding, StringComparison.Ordinal))
            {
                document.Notes.Add($"Line endings changed: {oldText.LineEnding} → {newText.LineEnding}.");
            }

            if (!string.Equals(oldText.EncodingName, newText.EncodingName, StringComparison.Ordinal))
            {
                document.Notes.Add($"Encoding changed: {oldText.EncodingName} → {newText.EncodingName}.");
            }

            var oldFinal = EndsWithNewline(oldBytes);
            var newFinal = EndsWithNewline(newBytes);
            if (oldFinal != newFinal)
            {
                document.Notes.Add(newFinal ? "Trailing newline added." : "Trailing newline removed.");
            }
        }

        var result = LineDiffer.Build(oldText.Lines, newText.Lines, contextLines);

        if (result.TooLarge)
        {
            document.TooLarge = true;
            document.UnavailableReason =
                $"File is too large for a line-by-line diff "
                + $"({oldText.Lines.Count + newText.Lines.Count:N0} lines; limit {LineDiffer.DefaultMaxLines:N0}).";
            return document;
        }

        document.Hunks = result.Hunks;
        document.AddedLines = result.AddedLines;
        document.RemovedLines = result.RemovedLines;
        return document;
    }

    private void ComputeLineStats(List<FileChange> changes, int contextLines)
    {
        foreach (var change in changes)
        {
            try
            {
                var document = BuildFileDiff(change, contextLines);
                if (document.HasContent)
                {
                    change.AddedLines = document.AddedLines;
                    change.RemovedLines = document.RemovedLines;
                }
            }
            catch (Exception exception)
            {
                Util.AppLog.Warn($"Line counts failed for '{change.Path}': {exception.Message}");
            }
        }
    }

    private static bool EndsWithNewline(byte[] content) =>
        content.Length > 0 && (content[^1] == (byte)'\n' || content[^1] == (byte)'\r');

    private static string DescribeUnavailable(FileChange change, string side) =>
        string.IsNullOrEmpty(side == "previous" ? change.OldHash : change.NewHash)
            ? $"The {side} content of this file was never captured — it was unreadable, binary, or larger than the capture limit."
            : $"The stored {side} content of this file is missing from the snapshot store.";

    private static bool LooksBinary(byte[] content) =>
        content.Length > 0 && FileClassifier.LooksBinary(content.AsSpan(0, Math.Min(content.Length, SniffLength)));
}
