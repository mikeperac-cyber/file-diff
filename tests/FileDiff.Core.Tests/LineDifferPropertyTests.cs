using FileDiff.Core.Diffing;

namespace FileDiff.Core.Tests;

/// <summary>
/// Brute-force validation of the diff engine.
/// <para>
/// For random inputs the produced edit script must (a) rebuild the old file from the new file and
/// vice versa, and (b) contain exactly as many changed lines as a reference LCS dynamic program
/// says are necessary. The second check is what proves the Myers implementation is really
/// minimal rather than merely plausible.
/// </para>
/// </summary>
public class LineDifferPropertyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(20260925)]
    public void Script_reconstructs_both_sides_and_is_minimal(int seed)
    {
        var random = new Random(seed);

        for (var iteration = 0; iteration < 150; iteration++)
        {
            var oldLines = RandomLines(random, random.Next(0, 42));
            var newLines = Mutate(random, oldLines, random.Next(0, 26));

            AssertValidEditScript(oldLines, newLines, LineDiffer.DefaultMaxEditDistance);
        }
    }

    [Fact]
    public void Anchor_fallback_still_produces_a_valid_script()
    {
        var random = new Random(31337);

        for (var iteration = 0; iteration < 120; iteration++)
        {
            var oldLines = RandomLines(random, random.Next(0, 60));
            var newLines = Mutate(random, oldLines, random.Next(1, 30));

            // Budget of zero forces every region through the anchor-splitting fallback.
            AssertValidEditScript(oldLines, newLines, maxEditDistance: 0, checkMinimality: false);
        }
    }

    [Fact]
    public void Unrelated_sequences_stay_consistent()
    {
        var random = new Random(5150);

        for (var iteration = 0; iteration < 60; iteration++)
        {
            AssertValidEditScript(RandomLines(random, 120), RandomLines(random, 120), LineDiffer.DefaultMaxEditDistance);
        }
    }

    private static void AssertValidEditScript(
        string[] oldLines,
        string[] newLines,
        int maxEditDistance,
        bool checkMinimality = true)
    {
        var script = LineDiffer.BuildFullScript(oldLines, newLines, maxEditDistance, out var added, out var removed);

        var rebuiltNew = script.Where(l => l.Kind != DiffLineKind.Removed).Select(l => l.Text).ToArray();
        var rebuiltOld = script.Where(l => l.Kind != DiffLineKind.Added).Select(l => l.Text).ToArray();

        Assert.Equal(oldLines, rebuiltOld);
        Assert.Equal(newLines, rebuiltNew);

        Assert.Equal(script.Count(l => l.Kind == DiffLineKind.Added), added);
        Assert.Equal(script.Count(l => l.Kind == DiffLineKind.Removed), removed);
        Assert.Equal(added + removed, script.Count(l => l.Kind != DiffLineKind.Context));

        var oldNumbers = script.Where(l => l.OldLineNumber.HasValue).Select(l => l.OldLineNumber!.Value).ToArray();
        Assert.Equal(Enumerable.Range(1, oldLines.Length), oldNumbers);

        var newNumbers = script.Where(l => l.NewLineNumber.HasValue).Select(l => l.NewLineNumber!.Value).ToArray();
        Assert.Equal(Enumerable.Range(1, newLines.Length), newNumbers);

        foreach (var line in script.Where(l => l.Kind == DiffLineKind.Context))
        {
            Assert.Equal(oldLines[line.OldLineNumber!.Value - 1], newLines[line.NewLineNumber!.Value - 1]);
            Assert.Equal(line.Text, oldLines[line.OldLineNumber!.Value - 1]);
        }

        if (checkMinimality && maxEditDistance >= oldLines.Length + newLines.Length)
        {
            Assert.Equal(ReferenceEditDistance(oldLines, newLines), added + removed);
        }
    }

    private static string[] RandomLines(Random random, int count)
    {
        // A deliberately small alphabet so duplicates (and therefore ambiguity) are common.
        string[] alphabet = ["alpha", "beta", "gamma", "delta"];

        return [.. Enumerable.Range(0, count).Select(_ => alphabet[random.Next(alphabet.Length)])];
    }

    private static string[] Mutate(Random random, string[] source, int operations)
    {
        var result = new List<string>(source);

        for (var i = 0; i < operations; i++)
        {
            switch (random.Next(4))
            {
                case 0 when result.Count > 0:
                    result.RemoveAt(random.Next(result.Count));
                    break;

                case 1:
                    result.Insert(random.Next(result.Count + 1), "new " + random.Next(4));
                    break;

                case 2 when result.Count > 0:
                    result[random.Next(result.Count)] = "edit " + random.Next(4);
                    break;

                case 3 when result.Count > 1:
                    var from = random.Next(result.Count);
                    var to = random.Next(result.Count);
                    (result[from], result[to]) = (result[to], result[from]);
                    break;
            }
        }

        return [.. result];
    }

    /// <summary>Reference line-change count: insertions + deletions implied by the LCS length.</summary>
    private static int ReferenceEditDistance(string[] a, string[] b)
    {
        var lengths = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i < a.Length; i++)
        {
            for (var j = 0; j < b.Length; j++)
            {
                lengths[i + 1, j + 1] = a[i] == b[j]
                    ? lengths[i, j] + 1
                    : Math.Max(lengths[i, j + 1], lengths[i + 1, j]);
            }
        }

        var lcs = lengths[a.Length, b.Length];
        return (a.Length - lcs) + (b.Length - lcs);
    }
}
