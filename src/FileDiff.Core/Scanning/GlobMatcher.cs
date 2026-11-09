namespace FileDiff.Core.Scanning;

/// <summary>
/// Glob matcher used for ignore rules.
/// <para>
/// A pattern without a separator is matched against <em>every</em> path segment, so
/// <c>node_modules</c> excludes that folder anywhere in the tree and <c>*.log</c> excludes
/// matching files anywhere. A pattern containing <c>/</c> is matched against the whole
/// relative path, where <c>**</c> spans separators, <c>*</c> stays inside one segment and
/// <c>?</c> matches a single character.
/// </para>
/// </summary>
public sealed class GlobMatcher
{
    private readonly List<Rule> _pathRules;
    private readonly List<System.Text.RegularExpressions.Regex> _segmentRules;

    public GlobMatcher(IEnumerable<string>? patterns)
    {
        _pathRules = [];
        _segmentRules = [];

        foreach (var pattern in patterns ?? [])
        {
            var cleaned = (pattern ?? string.Empty).Trim();
            if (cleaned.Length == 0 || cleaned.StartsWith('#'))
            {
                continue;
            }

            cleaned = cleaned.Replace('\\', '/').TrimStart('/');
            var needsSuffix = cleaned.EndsWith('/');
            cleaned = cleaned.TrimEnd('/');

            if (cleaned.Length == 0)
            {
                continue;
            }

            var regex = BuildRegex(cleaned, needsSuffix);
            if (cleaned.Contains('/'))
            {
                _pathRules.Add(new Rule(regex, true));
            }
            else
            {
                _segmentRules.Add(regex);
            }
        }
    }

    public bool IsEmpty => _pathRules.Count == 0 && _segmentRules.Count == 0;

    public bool Matches(string relativePath)
    {
        if ((_pathRules.Count == 0 && _segmentRules.Count == 0) || string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var normalized = Util.PathUtil.Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var rule in _pathRules)
        {
            if (rule.Regex.IsMatch(normalized))
            {
                return true;
            }
        }

        if (_segmentRules.Count == 0)
        {
            return false;
        }

        // Allocation-light segment walk: no Split array per file.
        var span = normalized.AsSpan();
        while (true)
        {
            var slash = span.IndexOf('/');
            var segment = slash < 0 ? span : span[..slash];
            if (segment.Length > 0)
            {
                var segmentString = segment.ToString();
                foreach (var regex in _segmentRules)
                {
                    if (regex.IsMatch(segmentString))
                    {
                        return true;
                    }
                }
            }

            if (slash < 0)
            {
                return false;
            }

            span = span[(slash + 1)..];
        }
    }

    /// <summary>
    /// True when a directory (and therefore its whole subtree) should be skipped.
    /// </summary>
    public bool ExcludesDirectory(string relativePath) => Matches(relativePath);

    private static System.Text.RegularExpressions.Regex BuildRegex(string pattern, bool directoryOnly)
    {
        var builder = new System.Text.StringBuilder("^");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            switch (c)
            {
                case '*':
                    var isDouble = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    if (isDouble)
                    {
                        i++;
                        // "**/" may also match nothing at all, e.g. "**/bin" matches "bin".
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            builder.Append("(?:.*/)?");
                        }
                        else
                        {
                            builder.Append(".*");
                        }
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    break;

                case '?':
                    builder.Append("[^/]");
                    break;

                default:
                    builder.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                    break;
            }
        }

        builder.Append(directoryOnly ? "(?:/.*)?$" : "$");

        return new System.Text.RegularExpressions.Regex(
            builder.ToString(),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.Compiled);
    }

    private readonly record struct Rule(
        System.Text.RegularExpressions.Regex Regex,
        bool IsPathPattern);
}
