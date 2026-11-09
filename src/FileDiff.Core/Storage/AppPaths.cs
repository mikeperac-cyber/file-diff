namespace FileDiff.Core.Storage;

/// <summary>Where the app keeps its own data. Never inside the folder being watched.</summary>
public static class AppPaths
{
    public const string ProductName = "FileDiff";

    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductName);

    public static string ProjectsRoot => Path.Combine(DataRoot, "projects");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public static string LogFile => Path.Combine(DataRoot, "filediff.log");

    public static void EnsureCreated() => Directory.CreateDirectory(DataRoot);
}
