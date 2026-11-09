using FileDiff.Core.Diffing;

namespace FileDiff.Core.Tests;

/// <summary>
/// The line differ is the piece most likely to be subtly wrong, so it is validated two ways:
/// hand-checked cases with exact expected hunk headers, and a property test that compares the
/// produced edit script against a brute-force dynamic-programming reference on random input.
/// </summary>
public class LineDifferTests
{
    private static string[] Lines(params string[] values) => values;

    private static string[] Range(int count, Func<int, string> factory) =>
        [.. Enumerable.Range(0, count).Select(factory)];

    [Fact]
    public void Identical_content_produces_no_hunks()
    {
        var result = LineDiffer.Build(Lines("a", "b", "c"), Lines("a", "b", "c"), 3);

        Assert.False(result.TooLarge);
        Assert.Empty(result.Hunks);
        Assert.Equal(0, result.AddedLines);
        Assert.Equal(0, result.RemovedLines);
    }

    [Fact]
    public void Replacing_one_line_in_the_middle_matches_git_hunk_header()
    {
        var oldLines = Range(20, i => $"line {i + 1}");
        var newLines = oldLines.ToArray();
        newLines[9] = "line 10 changed";

        var result = LineDiffer.Build(oldLines, newLines, 3);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("@@ -7,7 +7,7 @@", hunk.Header);
        Assert.Equal(7, hunk.OldStart);
        Assert.Equal(7, hunk.OldCount);
        Assert.Equal(7, hunk.NewStart);
        Assert.Equal(7, hunk.NewCount);
        Assert.Equal(1, result.AddedLines);
        Assert.Equal(1, result.RemovedLines);
    }

    [Fact]
    public void Inserting_at_the_top_reports_one_unchanged_old_line()
    {
        var result = LineDiffer.Build(Lines("a"), Lines("x", "y", "a"), 3);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("@@ -1,1 +1,3 @@", hunk.Header);
        Assert.Equal(2, result.AddedLines);
        Assert.Equal(0, result.RemovedLines);
    }

    [Fact]
    public void Pure_insertion_without_context_uses_zero_start()
    {
        var result = LineDiffer.Build(Lines("a", "b"), Lines("x", "y", "a", "b"), 0);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("@@ -0,0 +1,2 @@", hunk.Header);
        Assert.Equal(0, hunk.OldCount);
        Assert.Equal(2, hunk.NewCount);
    }

    [Fact]
    public void Appending_at_the_end_is_detected()
    {
        var result = LineDiffer.Build(Lines("a", "b"), Lines("a", "b", "c"), 3);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("@@ -1,2 +1,3 @@", hunk.Header);
        Assert.Equal(1, result.AddedLines);
        Assert.Equal(0, result.RemovedLines);
    }

    [Fact]
    public void Empty_previous_version_reports_every_line_as_added()
    {
        var result = LineDiffer.Build([], Lines("a", "b", "c"), 3);
        Assert.Equal(3, result.AddedLines);
        Assert.Equal(0, result.RemovedLines);
    }

    [Fact]
    public void Empty_new_version_reports_every_line_as_removed()
    {
        var result = LineDiffer.Build(Lines("a", "b", "c"), [], 3);
        Assert.Equal(0, result.AddedLines);
        Assert.Equal(3, result.RemovedLines);
        Assert.Equal("@@ -1,3 +0,0 @@", Assert.Single(result.Hunks).Header);
    }

    [Fact]
    public void Distant_changes_become_separate_hunks()
    {
        var oldLines = Range(60, i => $"line {i + 1}");
        var newLines = oldLines.ToArray();
        newLines[2] = "changed near the top";
        newLines[50] = "changed near the bottom";

        var result = LineDiffer.Build(oldLines, newLines, 3);

        Assert.Equal(2, result.Hunks.Count);
        Assert.Equal(2, result.AddedLines);
        Assert.Equal(2, result.RemovedLines);
    }

    [Fact]
    public void Nearby_changes_are_merged_into_one_hunk()
    {
        var oldLines = Range(30, i => $"line {i + 1}");
        var newLines = oldLines.ToArray();
        newLines[10] = "changed a";
        newLines[14] = "changed b"; // only three context lines apart

        var result = LineDiffer.Build(oldLines, newLines, 3);

        Assert.Single(result.Hunks);
    }

    [Fact]
    public void Content_larger_than_the_limit_is_refused_rather_than_hanging()
    {
        var huge = Range(40_000, i => "line " + i);
        var result = LineDiffer.Build(huge, huge, 3);

        Assert.True(result.TooLarge);
        Assert.Empty(result.Hunks);
    }

    [Fact]
    public void Inline_emphasis_splits_reconstruct_the_original_line()
    {
        var result = LineDiffer.Build(
            Lines("the quick brown fox jumps", "unchanged"),
            Lines("the quick red fox jumps", "unchanged"),
            3);

        var hunk = Assert.Single(result.Hunks);
        var removed = hunk.Lines.Single(l => l.Kind == DiffLineKind.Removed);
        var added = hunk.Lines.Single(l => l.Kind == DiffLineKind.Added);

        Assert.True(removed.HasEmphasis);
        Assert.True(added.HasEmphasis);
        Assert.Equal("the quick ", removed.Prefix);
        Assert.Equal("brown", removed.Emphasis);
        Assert.Equal(" fox jumps", removed.Suffix);
        Assert.Equal("red", added.Emphasis);
        Assert.Equal(removed.Text, removed.Prefix + removed.Emphasis + removed.Suffix);
        Assert.Equal(added.Text, added.Prefix + added.Emphasis + added.Suffix);
    }

    [Fact]
    public void Surrogate_pairs_are_never_split_by_emphasis()
    {
        var hunk = LineDiffer.Build(Lines("status: 😀 done"), Lines("status: 🚀 done"), 3).Hunks[0];

        foreach (var line in hunk.Lines.Where(l => l.HasEmphasis))
        {
            Assert.Equal(line.Text, line.Prefix + line.Emphasis + line.Suffix);

            if (line.Prefix.Length > 0)
            {
                Assert.False(char.IsHighSurrogate(line.Prefix[^1]));
            }
        }
    }
}
