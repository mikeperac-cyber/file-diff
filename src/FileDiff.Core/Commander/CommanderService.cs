using FileDiff.Core.Diffing;
using FileDiff.Core.Scanning;
using FileDiff.Core.Util;

namespace FileDiff.Core.Commander;

/// <summary>Outcome of a commander file operation.</summary>
public sealed record CommanderResult(bool Ok, string Message, string? FullPath = null);

/// <summary>Preview for F3 Quick View.</summary>
public sealed record QuickViewResult(
    bool Exists,
    bool IsDirectory,
    bool IsText,
    string PreviewText,
    long Size,
    bool Truncated,
    string? EncodingName);

/// <summary>
/// Orthodox-commander style file operations scoped to a watched root.
/// All relative paths use snapshot normalization (forward slashes).
/// Absolute destinations are allowed (second panel), but never escape
/// handling: relative sources must stay under <see cref="Root"/>.
/// </summary>
public sealed class CommanderService
{
    public const int QuickViewMaxBytes = 64 * 1024;
    public const int QuickViewMaxLines = 200;

    public CommanderService(string rootPath)
    {
        Root = PathUtil.NormalizeRoot(rootPath);
    }

    public string Root { get; }

    public string ResolveSource(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path is empty.", nameof(relativePath));
        }

        var normalized = PathUtil.Normalize(relativePath);
        var full = PathUtil.ToLocalPath(Root, normalized);
        if (!PathUtil.IsUnderRoot(Root, full))
        {
            throw new InvalidOperationException($"Refusing to escape watched folder: {relativePath}");
        }

        return full;
    }

    public string ResolveDestination(string destRelativeOrAbsolute)
    {
        if (string.IsNullOrWhiteSpace(destRelativeOrAbsolute))
        {
            throw new ArgumentException("Destination is empty.", nameof(destRelativeOrAbsolute));
        }

        var trimmed = destRelativeOrAbsolute.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return ResolveSource(trimmed);
    }

    public CommanderResult Copy(string sourceRelative, string destRelativeOrAbsolute, bool overwrite = false)
    {
        try
        {
            var src = ResolveSource(sourceRelative);
            if (!File.Exists(src))
            {
                return new CommanderResult(false, $"Source not found: {sourceRelative}");
            }

            var dest = ResolveDestination(destRelativeOrAbsolute);
            if (Directory.Exists(dest))
            {
                dest = Path.Combine(dest, Path.GetFileName(src));
            }

            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(src, dest, overwrite);
            return new CommanderResult(true, $"Copied to {dest}", dest);
        }
        catch (Exception ex)
        {
            return new CommanderResult(false, $"Copy failed: {ex.Message}");
        }
    }

    public CommanderResult Move(string sourceRelative, string destRelativeOrAbsolute, bool overwrite = false)
    {
        try
        {
            var src = ResolveSource(sourceRelative);
            if (!File.Exists(src) && !Directory.Exists(src))
            {
                return new CommanderResult(false, $"Source not found: {sourceRelative}");
            }

            var dest = ResolveDestination(destRelativeOrAbsolute);
            if (Directory.Exists(dest))
            {
                dest = Path.Combine(dest, Path.GetFileName(src.TrimEnd('/', '\\')));
            }

            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(src))
            {
                if (File.Exists(dest) && !overwrite)
                {
                    return new CommanderResult(false, $"Destination exists: {dest}");
                }

                if (overwrite && File.Exists(dest))
                {
                    File.Delete(dest);
                }

                File.Move(src, dest);
            }
            else
            {
                if (Directory.Exists(dest) && !overwrite)
                {
                    return new CommanderResult(false, $"Destination exists: {dest}");
                }

                Directory.Move(src, dest);
            }

            return new CommanderResult(true, $"Moved to {dest}", dest);
        }
        catch (Exception ex)
        {
            return new CommanderResult(false, $"Move failed: {ex.Message}");
        }
    }

    public CommanderResult Delete(string relativePath, bool recursive = false)
    {
        try
        {
            var full = ResolveSource(relativePath);
            if (File.Exists(full))
            {
                File.Delete(full);
                return new CommanderResult(true, $"Deleted {relativePath}", full);
            }

            if (Directory.Exists(full))
            {
                if (!recursive && Directory.EnumerateFileSystemEntries(full).Any())
                {
                    return new CommanderResult(false, "Folder is not empty (use recursive delete).");
                }

                Directory.Delete(full, recursive);
                return new CommanderResult(true, $"Deleted folder {relativePath}", full);
            }

            return new CommanderResult(false, $"Not found: {relativePath}");
        }
        catch (Exception ex)
        {
            return new CommanderResult(false, $"Delete failed: {ex.Message}");
        }
    }

    public CommanderResult CreateDirectory(string relativePath)
    {
        try
        {
            var full = ResolveSource(relativePath);
            Directory.CreateDirectory(full);
            return new CommanderResult(true, $"Created {relativePath}", full);
        }
        catch (Exception ex)
        {
            return new CommanderResult(false, $"Mkdir failed: {ex.Message}");
        }
    }

    public CommanderResult Rename(string sourceRelative, string newName)
    {
        try
        {
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || newName.Contains('/') || newName.Contains('\\'))
            {
                var normalized = PathUtil.Normalize(newName);
                if (normalized.Contains('/'))
                {
                    return Move(sourceRelative, normalized);
                }

                return new CommanderResult(false, $"Invalid name: {newName}");
            }

            var src = ResolveSource(sourceRelative);
            var parent = Path.GetDirectoryName(src);
            if (parent is null)
            {
                return new CommanderResult(false, "Cannot rename root.");
            }

            var dest = Path.Combine(parent, newName);
            if (!PathUtil.IsUnderRoot(Root, dest))
            {
                return new CommanderResult(false, "Refusing to escape watched folder.");
            }

            if (File.Exists(src))
            {
                File.Move(src, dest);
            }
            else if (Directory.Exists(src))
            {
                Directory.Move(src, dest);
            }
            else
            {
                return new CommanderResult(false, $"Not found: {sourceRelative}");
            }

            return new CommanderResult(true, $"Renamed to {newName}", dest);
        }
        catch (Exception ex)
        {
            return new CommanderResult(false, $"Rename failed: {ex.Message}");
        }
    }

    public QuickViewResult QuickView(string relativePath)
    {
        string full;
        try
        {
            full = ResolveSource(relativePath);
        }
        catch
        {
            return new QuickViewResult(false, false, false, string.Empty, 0, false, null);
        }

        if (Directory.Exists(full))
        {
            var entries = Directory.EnumerateFileSystemEntries(full)
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(QuickViewMaxLines + 1)
                .ToList();
            var truncated = entries.Count > QuickViewMaxLines;
            var text = string.Join(Environment.NewLine, entries.Take(QuickViewMaxLines));
            return new QuickViewResult(true, true, true, text, 0, truncated, null);
        }

        if (!File.Exists(full))
        {
            return new QuickViewResult(false, false, false, string.Empty, 0, false, null);
        }

        try
        {
            var info = new FileInfo(full);
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var toRead = (int)Math.Min(info.Length, QuickViewMaxBytes + 8192);
            var buffer = new byte[toRead];
            var read = 0;
            while (read < toRead)
            {
                var n = stream.Read(buffer, read, toRead - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            var sample = buffer.AsSpan(0, Math.Min(read, 8000));
            if (read > 0 && FileClassifier.LooksBinary(sample))
            {
                return new QuickViewResult(true, false, false, "Binary file — no preview.", info.Length, true, null);
            }

            var bytes = buffer[..read];
            var decoded = TextDecoder.Decode(bytes);
            var lines = decoded.Lines;
            var truncated = read >= QuickViewMaxBytes + 8192 - 1024 || lines.Count > QuickViewMaxLines;
            var preview = string.Join(Environment.NewLine, lines.Take(QuickViewMaxLines));
            return new QuickViewResult(true, false, true, preview, info.Length, truncated, decoded.EncodingName);
        }
        catch
        {
            return new QuickViewResult(false, false, false, string.Empty, 0, false, null);
        }
    }
}
