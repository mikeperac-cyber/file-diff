using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileDiff.Core.Storage;

/// <summary>
/// JSON read/write with crash-safe writes: content goes to a temp file first and is then
/// swapped into place, so an interrupted save can never leave a half-written snapshot index.
/// </summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(true);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static T? TryRead<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException)
        {
            // Corrupt or truncated file: quarantine it so the next save can start clean.
            TryQuarantine(path);
            return default;
        }
        catch (IOException)
        {
            return default;
        }
    }

    public static T TryReadOrNew<T>(string path)
        where T : new() => TryRead<T>(path) ?? new T();

    private static void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, $"{path}.corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Nothing useful to do; the next write replaces the file anyway.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
