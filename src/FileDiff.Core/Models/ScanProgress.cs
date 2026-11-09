namespace FileDiff.Core.Models;

public enum ScanPhase
{
    Enumerating,
    Hashing,
    Finalizing,
    Completed,
}

/// <summary>Progress heartbeat raised while a scan runs, so the UI can stay responsive and cancellable.</summary>
public sealed record ScanProgress(
    ScanPhase Phase,
    int Processed,
    int Total,
    string CurrentItem)
{
    /// <summary>0..1, or null while the total is not yet known.</summary>
    public double? Fraction => Total > 0 ? Math.Clamp(Processed / (double)Total, 0, 1) : null;

    public string Describe() => Phase switch
    {
        ScanPhase.Enumerating => $"Enumerating folder… {CurrentItem}",
        ScanPhase.Hashing => $"Reading files… {Processed} / {Total} — {CurrentItem}",
        ScanPhase.Finalizing => "Writing snapshot…",
        _ => "Done",
    };
}
