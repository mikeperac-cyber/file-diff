using FileDiff.Core.Diffing;

namespace FileDiff.Core.Export;

/// <summary>
/// Everything an exporter needs: the comparison, plus a way to fetch the content diff for a file
/// (which is lazy so that exporting a large folder stays cheap).
/// </summary>
public sealed class ExportRequest
{
    public required DiffResult Diff { get; init; }

    public required Func<FileChange, FileDiffDocument> GetDocument { get; init; }

    public string BaselineLabel { get; init; } = "previous snapshot";

    public string TargetLabel { get; init; } = "current snapshot";

    public string AppName { get; init; } = "FileDiff";

    /// <summary>Extra note rendered at the top of reports, e.g. that the target was a live scan.</summary>
    public string? Note { get; init; }
}
