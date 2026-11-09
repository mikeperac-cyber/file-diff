namespace FileDiff.Core.Tests;

/// <summary>
/// A throwaway folder plus a throwaway app-data root, deleted when the test ends.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "FileDiffTests", Guid.NewGuid().ToString("N"));
        WatchRoot = Path.Combine(Root, "watched");
        DataRoot = Path.Combine(Root, "appdata");

        Directory.CreateDirectory(WatchRoot);
        Directory.CreateDirectory(DataRoot);
    }

    public string Root { get; }

    /// <summary>The folder being watched — treated as read-only by the app.</summary>
    public string WatchRoot { get; }

    /// <summary>Stands in for %APPDATA%\FileDiff so tests never touch real user data.</summary>
    public string DataRoot { get; }

    public string Write(string relativePath, string content)
    {
        var path = Path.Combine(WatchRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteBytes(string relativePath, byte[] content)
    {
        var path = Path.Combine(WatchRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string PathOf(string relativePath) =>
        Path.Combine(WatchRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Delete(string relativePath) => File.Delete(PathOf(relativePath));

    public void Move(string from, string to)
    {
        var destination = PathOf(to);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(PathOf(from), destination);
    }

    public FileDiff.Core.Models.ScanOptions NewScanOptions() => new()
    {
        RootPath = WatchRoot,
        MaxTextFileBytes = 1024 * 1024,
        CaptureContent = true,
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
