namespace FileDiff.Core.Util;

/// <summary>Minimal append-only log. Used so unexpected failures leave a trace without ever throwing.</summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(Storage.AppPaths.LogFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            lock (Gate)
            {
                File.AppendAllText(Storage.AppPaths.LogFile, line);
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
