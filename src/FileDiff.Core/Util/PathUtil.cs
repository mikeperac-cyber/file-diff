namespace FileDiff.Core.Util;

/// <summary>
/// Helpers for keeping every path that is persisted inside a snapshot in a single,
/// predictable shape: forward slashes, no trailing separator, no leading "./".
/// Snapshots therefore compare identically no matter which separator the OS handed us.
/// </summary>
public static class PathUtil
{
    /// <summary>Canonical form used as the snapshot dictionary key and in diffs.</summary>
    public static string Normalize(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        // Fast path: enumeration and diff paths are already normalized (forward
        // slashes, no trailing separator). Avoids a string allocation per file.
        if (relativePath.IndexOf('\\') < 0
            && !relativePath.EndsWith('/')
            && !relativePath.StartsWith("./", StringComparison.Ordinal))
        {
            return relativePath;
        }

        var value = relativePath.Replace('\\', '/');

        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.TrimEnd('/');
    }

    /// <summary>Absolute, fully resolved path for a user-supplied folder.</summary>
    public static string NormalizeRoot(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Turns a normalized relative path back into a path the OS can open.</summary>
    public static string ToLocalPath(string root, string relativePath)
    {
        var combined = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFullPath(combined);
    }

    /// <summary>True when <paramref name="candidate"/> is the root itself or sits underneath it.</summary>
    public static bool IsUnderRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || string.Equals(NormalizeRoot(root), normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Human readable byte count, e.g. "1.4 MB".</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.#} {units[unit]}";
    }
}
