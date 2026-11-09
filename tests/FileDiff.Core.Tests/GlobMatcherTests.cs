using FileDiff.Core.Scanning;

namespace FileDiff.Core.Tests;

public class GlobMatcherTests
{
    [Theory]
    // A pattern with no separator matches any path segment, so it prunes whole subtrees.
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("node_modules", "node_modules/react/index.js", true)]
    [InlineData("node_modules", "src/node_modules/a.js", true)]
    [InlineData("node_modules", "node_modules_backup/a.js", false)]
    [InlineData("node_modules", "src/Node_Modules/a.js", true)]
    [InlineData("*.log", "build.log", true)]
    [InlineData("*.log", "logs/build.log", true)]
    [InlineData("*.log", "build.logx", false)]
    [InlineData("*.log", "build.log/keep.txt", true)]
    // A pattern with a separator matches the whole relative path.
    [InlineData("src/generated/**", "src/generated/a/b.txt", true)]
    [InlineData("src/generated/**", "src/generated.txt", false)]
    [InlineData("docs/*.md", "docs/readme.md", true)]
    [InlineData("docs/*.md", "docs/sub/readme.md", false)]
    [InlineData("**/bin", "bin", true)]
    [InlineData("**/bin", "a/b/bin", true)]
    [InlineData("**/bin", "binder", false)]
    [InlineData("cache?", "cache1", true)]
    [InlineData("cache?", "cache12", false)]
    public void Patterns_match_as_documented(string pattern, string path, bool expected)
    {
        var matcher = new GlobMatcher([pattern]);
        Assert.Equal(expected, matcher.Matches(path));
    }

    [Fact]
    public void Trailing_slash_pattern_also_covers_the_subtree()
    {
        var matcher = new GlobMatcher(["artifacts/"]);

        Assert.True(matcher.Matches("artifacts"));
        Assert.True(matcher.Matches("artifacts/deep/nested/file.txt"));
        Assert.False(matcher.Matches("myartifacts/file.txt"));
    }

    [Fact]
    public void Blank_and_comment_patterns_are_ignored()
    {
        var matcher = new GlobMatcher(["", "   ", "# a comment", "real"]);

        Assert.True(matcher.Matches("real"));
        Assert.False(matcher.Matches("# a comment"));
    }

    [Fact]
    public void Backslashes_are_treated_as_separators()
    {
        var matcher = new GlobMatcher([@"temp\cache"]);

        // A Windows-style separator in a pattern behaves exactly like a forward slash, and
        // matching a folder is enough to prune everything beneath it during a scan.
        Assert.True(matcher.Matches("temp/cache"));
        Assert.False(matcher.Matches("temp"));
    }

    [Fact]
    public void Path_patterns_can_cover_a_whole_subtree_with_a_double_star()
    {
        var matcher = new GlobMatcher([@"temp\cache\**"]);

        Assert.True(matcher.Matches("temp/cache/one.txt"));
        Assert.True(matcher.Matches("temp/cache/deep/one.txt"));
        Assert.False(matcher.Matches("temp/other/one.txt"));
    }

    [Fact]
    public void Default_rules_exclude_build_output_but_not_source()
    {
        var matcher = new GlobMatcher(IgnoreRules.Combine(null));

        Assert.True(matcher.Matches(".git/config"));
        Assert.True(matcher.Matches("src/bin/Debug/app.dll"));
        Assert.True(matcher.Matches("src/obj/project.assets.json"));

        Assert.False(matcher.Matches("src/Program.cs"));
        Assert.False(matcher.Matches("src/binary/Search.cs"));
        Assert.False(matcher.Matches("README.md"));
    }

    [Fact]
    public void Combine_keeps_user_patterns_and_removes_duplicates()
    {
        var combined = IgnoreRules.Combine(["mycache/", "NODE_MODULES/", "*.bak"]);

        Assert.Contains("mycache/", combined);
        Assert.Contains("*.bak", combined);
        Assert.Equal(1, combined.Count(p => string.Equals(p, "node_modules/", StringComparison.OrdinalIgnoreCase)));
    }
}
