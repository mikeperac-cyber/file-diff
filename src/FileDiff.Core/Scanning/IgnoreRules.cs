namespace FileDiff.Core.Scanning;

/// <summary>
/// Default noise filters. These keep snapshots focused on things a human actually changed,
/// instead of build output and source-control internals.
/// </summary>
public static class IgnoreRules
{
    public static readonly string[] DefaultPatterns =
    [
        // Source control / editor internals
        ".git/",
        ".svn/",
        ".hg/",
        ".vs/",
        ".vscode/",
        ".idea/",
        ".fleet/",

        // Build output
        "bin/",
        "obj/",
        "node_modules/",
        "dist/",
        "build/",
        "out/",
        "target/",
        "__pycache__/",
        ".pytest_cache/",
        ".venv/",
        "venv/",
        ".next/",
        ".nuxt/",
        ".gradle/",
        ".mypy_cache/",
        ".ruff_cache/",
        "packages/",
        "TestResults/",

        // OS noise
        "$RECYCLE.BIN/",
        "System Volume Information/",
        "Thumbs.db",
        "desktop.ini",
        ".DS_Store",

        // Transient files
        "*.tmp",
        "*.temp",
        "*.log",
        "*.swp",
        "*.swo",
        "*~",
        "*.bak",
        "*.orig",
        "*.rej",
        "~$*",
    ];

    /// <summary>Defaults first, then user extras (later patterns cannot un-ignore, but order is stable for display).</summary>
    public static List<string> Combine(IEnumerable<string>? userPatterns)
    {
        var combined = new List<string>(DefaultPatterns.Length + 8);
        combined.AddRange(DefaultPatterns);

        foreach (var pattern in userPatterns ?? [])
        {
            var cleaned = (pattern ?? string.Empty).Trim();
            if (cleaned.Length > 0
                && !cleaned.StartsWith('#')
                && !combined.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                combined.Add(cleaned);
            }
        }

        return combined;
    }
}
