namespace FileDiff.Core.Models;

/// <summary>
/// A watched folder. The project key is derived from the folder path, so sharing the same
/// path again always continues the same snapshot timeline.
/// </summary>
public sealed class ProjectInfo
{
    public string ProjectKey { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastScannedAtUtc { get; set; }

    public int SnapshotCount { get; set; }
}
