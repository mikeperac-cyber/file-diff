namespace FileDiff.Core.Diffing;

/// <summary>
/// Line-level diff engine.
/// <para>
/// Uses Myers' O(ND) algorithm with three practical safeguards so that real-world folders
/// (minified assets, generated files, complete rewrites) still produce useful output: common
/// leading/trailing lines are stripped first, the search is bounded by an edit-distance budget
/// with a memory-bounded trace, and when that budget is exhausted the region is split at an
/// anchor line that is rare in both sides and the halves are diffed recursively.
/// </para>
/// </summary>
public static class LineDiffer
{
    public const int DefaultMaxLines = 60_000;
    public const int DefaultMaxEditDistance = 8_000;

    private const int MaxRecursionDepth = 24;

    /// <summary>Diffs two sets of lines and returns hunks with the requested amount of context.</summary>
    public static LineDiffResult Build(
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines,
        int contextLines,
        int maxLines = DefaultMaxLines,
        int maxEditDistance = DefaultMaxEditDistance)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);

        if ((long)oldLines.Count + newLines.Count > maxLines)
        {
            return LineDiffResult.CreateTooLarge();
        }

        var script = BuildFullScript(oldLines, newLines, maxEditDistance, out var added, out var removed);

        return new LineDiffResult
        {
            Hunks = BuildHunks(script, Math.Max(0, contextLines)),
            AddedLines = added,
            RemovedLines = removed,
        };
    }

    /// <summary>Full, un-contexted edit script. Internal so tests can verify minimality against a reference.</summary>
    internal static List<DiffLine> BuildFullScript(
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines,
        int maxEditDistance,
        out int added,
        out int removed)
    {
        var interned = new Dictionary<string, int>(Math.Min(oldLines.Count + newLines.Count, 65536), StringComparer.Ordinal);
        var a = Intern(oldLines, interned);
        var b = Intern(newLines, interned);

        var ops = new List<Op>(a.Length + b.Length);
        DiffRange(a, 0, a.Length, b, 0, b.Length, 0, maxEditDistance, ops);

        return BuildScript(ops, oldLines, newLines, out added, out removed);
    }

    private static int[] Intern(IReadOnlyList<string> lines, Dictionary<string, int> map)
    {
        var result = new int[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var value = lines[i] ?? string.Empty;
            if (!map.TryGetValue(value, out var id))
            {
                id = map.Count;
                map[value] = id;
            }

            result[i] = id;
        }

        return result;
    }

    /// <summary>Recursively diffs the given ranges of the shared interned arrays.</summary>
    private static void DiffRange(
        int[] a,
        int aStart,
        int aLen,
        int[] b,
        int bStart,
        int bLen,
        int depth,
        int maxEditDistance,
        List<Op> output)
    {
        if (aLen <= 0 && bLen <= 0)
        {
            return;
        }

        var prefix = 0;
        while (prefix < aLen && prefix < bLen && a[aStart + prefix] == b[bStart + prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < aLen - prefix
               && suffix < bLen - prefix
               && a[aStart + aLen - 1 - suffix] == b[bStart + bLen - 1 - suffix])
        {
            suffix++;
        }

        for (var i = 0; i < prefix; i++)
        {
            output.Add(Equal(aStart + i, bStart + i));
        }

        var midA = aLen - prefix - suffix;
        var midB = bLen - prefix - suffix;

        if (midA <= 0)
        {
            for (var j = 0; j < midB; j++)
            {
                output.Add(Insert(bStart + prefix + j));
            }
        }
        else if (midB <= 0)
        {
            for (var i = 0; i < midA; i++)
            {
                output.Add(Remove(aStart + prefix + i));
            }
        }
        else
        {
            DiffMiddle(a, aStart + prefix, midA, b, bStart + prefix, midB, depth, maxEditDistance, output);
        }

        for (var i = 0; i < suffix; i++)
        {
            output.Add(Equal(aStart + aLen - suffix + i, bStart + bLen - suffix + i));
        }
    }

    private static void DiffMiddle(
        int[] a,
        int aStart,
        int aLen,
        int[] b,
        int bStart,
        int bLen,
        int depth,
        int maxEditDistance,
        List<Op> output)
    {
        var segA = new int[aLen];
        Array.Copy(a, aStart, segA, 0, aLen);
        var segB = new int[bLen];
        Array.Copy(b, bStart, segB, 0, bLen);

        var script = Myers(segA, segB, maxEditDistance);
        if (script is not null)
        {
            foreach (var op in script)
            {
                output.Add(op.Kind switch
                {
                    DiffLineKind.Context => Equal(aStart + op.OldIndex, bStart + op.NewIndex),
                    DiffLineKind.Removed => Remove(aStart + op.OldIndex),
                    _ => Insert(bStart + op.NewIndex),
                });
            }

            return;
        }

        var anchor = depth < MaxRecursionDepth ? FindAnchor(segA, segB) : null;

        if (anchor is null)
        {
            // Budget exhausted and no usable anchor: report an honest full replacement.
            for (var i = 0; i < aLen; i++)
            {
                output.Add(Remove(aStart + i));
            }

            for (var j = 0; j < bLen; j++)
            {
                output.Add(Insert(bStart + j));
            }

            return;
        }

        var (anchorA, anchorB) = anchor.Value;

        DiffRange(a, aStart, anchorA, b, bStart, anchorB, depth + 1, maxEditDistance, output);
        output.Add(Equal(aStart + anchorA, bStart + anchorB));
        DiffRange(
            a,
            aStart + anchorA + 1,
            aLen - anchorA - 1,
            b,
            bStart + anchorB + 1,
            bLen - anchorB - 1,
            depth + 1,
            maxEditDistance,
            output);
    }

    /// <summary>
    /// Picks a line that occurs rarely in both sides and exists in both — the patience-diff
    /// heuristic. Anchoring on it splits a hopeless region into two tractable ones.
    /// </summary>
    private static (int AnchorA, int AnchorB)? FindAnchor(int[] a, int[] b)
    {
        var countsA = CountOccurrences(a);
        var countsB = CountOccurrences(b);

        var bestIndex = -1;
        long bestScore = long.MaxValue;

        for (var i = 0; i < a.Length; i++)
        {
            var value = a[i];
            if (!countsB.TryGetValue(value, out var inB) || inB == 0)
            {
                continue;
            }

            var score = (long)countsA[value] * inB;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;

                if (score <= 1)
                {
                    break;
                }
            }
        }

        if (bestIndex < 0)
        {
            return null;
        }

        var target = a[bestIndex];
        for (var j = 0; j < b.Length; j++)
        {
            if (b[j] == target)
            {
                return (bestIndex, j);
            }
        }

        return null;
    }

    private static Dictionary<int, int> CountOccurrences(int[] values)
    {
        var counts = new Dictionary<int, int>(Math.Min(values.Length, 8192));

        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
    }

    private static Op Equal(int oldIndex, int newIndex) => new(DiffLineKind.Context, oldIndex, newIndex);

    private static Op Remove(int oldIndex) => new(DiffLineKind.Removed, oldIndex, -1);

    private static Op Insert(int newIndex) => new(DiffLineKind.Added, -1, newIndex);

    private readonly record struct Op(DiffLineKind Kind, int OldIndex, int NewIndex);

    /// <summary>
    /// Myers' O(ND) shortest-edit-script search. Returns null when the difference needs more than
    /// <paramref name="maxEditDistance"/> edits, so the caller can fall back to anchor splitting.
    /// The trace only keeps the band each iteration actually touched, which keeps memory
    /// quadratic in the edit distance rather than in the file length.
    /// </summary>
    private static List<Op>? Myers(int[] a, int[] b, int maxEditDistance)
    {
        var n = a.Length;
        var m = b.Length;
        var max = n + m;

        if (max == 0)
        {
            return [];
        }

        if (maxEditDistance <= 0)
        {
            return null;
        }

        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var dLimit = Math.Min(max, maxEditDistance);
        var foundAt = -1;

        for (var d = 0; d <= dLimit; d++)
        {
            var snapshot = new int[2 * d + 3];
            for (var k = -d - 1; k <= d + 1; k++)
            {
                var index = offset + k;
                snapshot[k + d + 1] = index >= 0 && index < v.Length ? v[index] : 0;
            }

            trace.Add(snapshot);

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1];
                }
                else
                {
                    x = v[offset + k - 1] + 1;
                }

                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    foundAt = d;
                    break;
                }
            }

            if (foundAt >= 0)
            {
                break;
            }
        }

        return foundAt < 0 ? null : Backtrack(a.Length, b.Length, trace, foundAt);
    }

    private static List<Op> Backtrack(int n, int m, List<int[]> trace, int foundAt)
    {
        var ops = new List<Op>(n + m);
        var x = n;
        var y = m;

        for (var d = foundAt; d > 0; d--)
        {
            var snapshot = trace[d];
            var k = x - y;

            int previousK;
            if (k == -d || (k != d && snapshot[k - 1 + d + 1] < snapshot[k + 1 + d + 1]))
            {
                previousK = k + 1;
            }
            else
            {
                previousK = k - 1;
            }

            var previousX = snapshot[previousK + d + 1];
            var previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                x--;
                y--;
                ops.Add(Equal(x, y));
            }

            if (x == previousX)
            {
                y--;
                ops.Add(Insert(y));
            }
            else
            {
                x--;
                ops.Add(Remove(x));
            }
        }

        while (x > 0 && y > 0)
        {
            x--;
            y--;
            ops.Add(Equal(x, y));
        }

        ops.Reverse();
        return ops;
    }

    private static List<DiffLine> BuildScript(
        List<Op> ops,
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines,
        out int added,
        out int removed)
    {
        var script = new List<DiffLine>(ops.Count);
        added = 0;
        removed = 0;

        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case DiffLineKind.Context:
                    script.Add(new DiffLine
                    {
                        Kind = DiffLineKind.Context,
                        Text = oldLines[op.OldIndex],
                        OldLineNumber = op.OldIndex + 1,
                        NewLineNumber = op.NewIndex + 1,
                    });
                    break;

                case DiffLineKind.Removed:
                    removed++;
                    script.Add(new DiffLine
                    {
                        Kind = DiffLineKind.Removed,
                        Text = oldLines[op.OldIndex],
                        OldLineNumber = op.OldIndex + 1,
                    });
                    break;

                default:
                    added++;
                    script.Add(new DiffLine
                    {
                        Kind = DiffLineKind.Added,
                        Text = newLines[op.NewIndex],
                        NewLineNumber = op.NewIndex + 1,
                    });
                    break;
            }
        }

        return script;
    }

    /// <summary>
    /// Groups the edit script into hunks of <paramref name="contextLines"/> surrounding context,
    /// merging blocks whose context would otherwise overlap — the same shape
    /// <c>git diff</c> produces, which is what makes the patch export reusable.
    /// </summary>
    private static List<DiffHunk> BuildHunks(List<DiffLine> script, int contextLines)
    {
        var hunks = new List<DiffHunk>();
        if (script.Count == 0)
        {
            return hunks;
        }

        var blocks = new List<(int Start, int End)>();
        var i = 0;

        while (i < script.Count)
        {
            if (script[i].Kind == DiffLineKind.Context)
            {
                i++;
                continue;
            }

            var start = i;
            while (i < script.Count && script[i].Kind != DiffLineKind.Context)
            {
                i++;
            }

            blocks.Add((start, i - 1));
        }

        if (blocks.Count == 0)
        {
            return hunks;
        }

        var merged = new List<(int Start, int End)> { blocks[0] };
        for (var b = 1; b < blocks.Count; b++)
        {
            var previous = merged[^1];
            if (blocks[b].Start - previous.End - 1 <= contextLines * 2)
            {
                merged[^1] = (previous.Start, blocks[b].End);
            }
            else
            {
                merged.Add(blocks[b]);
            }
        }

        foreach (var (blockStart, blockEnd) in merged)
        {
            var from = Math.Max(0, blockStart - contextLines);
            var to = Math.Min(script.Count - 1, blockEnd + contextLines);
            var lines = script.GetRange(from, to - from + 1);

            ApplyInlineEmphasis(lines);

            hunks.Add(new DiffHunk
            {
                // A hunk that only inserts has no old line numbers at all, so fall back to the
                // last line before the hunk (0 at the top of the file) — matching git's output.
                OldStart = lines.FirstOrDefault(l => l.OldLineNumber.HasValue)?.OldLineNumber
                           ?? LastNumberBefore(script, from, l => l.OldLineNumber),
                OldCount = lines.Count(l => l.OldLineNumber.HasValue),
                NewStart = lines.FirstOrDefault(l => l.NewLineNumber.HasValue)?.NewLineNumber
                           ?? LastNumberBefore(script, from, l => l.NewLineNumber),
                NewCount = lines.Count(l => l.NewLineNumber.HasValue),
                Lines = lines,
            });
        }

        return hunks;
    }

    private static int LastNumberBefore(List<DiffLine> script, int index, Func<DiffLine, int?> selector)
    {
        for (var i = Math.Min(index, script.Count) - 1; i >= 0; i--)
        {
            var value = selector(script[i]);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        return 0;
    }

    /// <summary>
    /// Pairs removed lines with the added lines that follow them, index by index, and works out
    /// the unchanged frame around the change. That frame is what lets the UI highlight only the
    /// part of the line that actually differs.
    /// </summary>
    private static void ApplyInlineEmphasis(List<DiffLine> lines)
    {
        var i = 0;

        while (i < lines.Count)
        {
            if (lines[i].Kind == DiffLineKind.Context)
            {
                i++;
                continue;
            }

            var removedStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed)
            {
                i++;
            }

            var removedCount = i - removedStart;

            var addedStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Added)
            {
                i++;
            }

            var addedCount = i - addedStart;

            var pairs = Math.Min(removedCount, addedCount);
            for (var p = 0; p < pairs; p++)
            {
                SplitEmphasis(lines[removedStart + p], lines[addedStart + p]);
            }
        }
    }

    private static void SplitEmphasis(DiffLine oldLine, DiffLine newLine)
    {
        var oldText = oldLine.Text;
        var newText = newLine.Text;
        var shared = Math.Min(oldText.Length, newText.Length);

        var prefix = 0;
        while (prefix < shared && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < shared - prefix
               && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
        {
            suffix++;
        }

        // Never cut a surrogate pair in half: prefix + emphasis + suffix must always
        // concatenate back to exactly the original text.
        while (prefix > 0 && (char.IsHighSurrogate(oldText[prefix - 1]) || char.IsHighSurrogate(newText[prefix - 1])))
        {
            prefix--;
        }

        while (suffix > 0
               && (char.IsLowSurrogate(oldText[oldText.Length - suffix])
                   || char.IsLowSurrogate(newText[newText.Length - suffix])))
        {
            suffix--;
        }

        Apply(oldLine, oldText, prefix, suffix);
        Apply(newLine, newText, prefix, suffix);
    }

    private static void Apply(DiffLine line, string text, int prefix, int suffix)
    {
        line.Prefix = text[..prefix];
        line.Emphasis = text[prefix..(text.Length - suffix)];
        line.Suffix = suffix == 0 ? string.Empty : text[(text.Length - suffix)..];
        line.HasEmphasis = line.Emphasis.Length > 0;
    }
}
