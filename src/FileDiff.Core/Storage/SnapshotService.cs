using FileDiff.Core.Diffing;
using FileDiff.Core.Models;
using FileDiff.Core.Scanning;
using FileDiff.Core.Util;

namespace FileDiff.Core.Storage;

/// <summary>Outcome of one capture run.</summary>
public sealed record SnapshotCapture(
    ProjectInfo Project,
    ScanManifest Manifest,
    ScanManifest? Previous,
    bool Saved,
    RetentionResult? Retention)
{
    /// <summary>True when the folder was only compared and nothing was written to the timeline.</summary>
    public bool IsLiveScan => !Saved;
}

/// <summary>
/// The single entry point the UI uses. Hides the sequence
/// "resolve project → scan → save snapshot → prune old snapshots" behind two methods.
/// </summary>
public sealed class SnapshotService
{
    public SnapshotService(SnapshotStore store) => Store = store ?? throw new ArgumentNullException(nameof(store));

    public SnapshotStore Store { get; }

    /// <summary>
    /// Scans the folder. When <paramref name="saveSnapshot"/> is true the result is appended to the
    /// timeline and retention is applied; otherwise it is an ephemeral "what changed right now?"
    /// check that leaves the timeline untouched.
    /// </summary>
    public async Task<SnapshotCapture> CaptureAsync(
        ScanOptions options,
        bool saveSnapshot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = PathUtil.NormalizeRoot(options.RootPath);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"That folder does not exist: {root}");
        }

        if (PathUtil.IsUnderRoot(Store.DataRoot, root))
        {
            throw new InvalidOperationException(
                "That folder is where FileDiff stores its own snapshots. Pick the folder you want to watch instead.");
        }

        var project = Store.LoadOrCreateProject(root);
        var blobs = Store.GetBlobStore(project.ProjectKey);
        var previous = LoadLatestManifest(project.ProjectKey);
        var startedAt = DateTimeOffset.UtcNow;

        var scanner = new Scanner(blobs);
        var manifest = await scanner.ScanAsync(options, previous, progress, cancellationToken).ConfigureAwait(false);
        manifest.CreatedAtUtc = startedAt;

        if (!saveSnapshot)
        {
            // Blobs written during a live check are content-addressed, so they are reused (not
            // duplicated) if the user decides to keep this state as a snapshot afterwards.
            return new SnapshotCapture(project, manifest, previous, false, null);
        }

        manifest.SnapshotId = Store.CreateSnapshotId(project.ProjectKey, startedAt);
        await Store.SaveSnapshotAsync(project.ProjectKey, manifest, cancellationToken).ConfigureAwait(false);

        var retention = await Store
            .ApplyRetentionAsync(project.ProjectKey, options.MaxSnapshotsToKeep, cancellationToken)
            .ConfigureAwait(false);

        var project2 = Store.LoadOrCreateProject(root);
        return new SnapshotCapture(project2, manifest, previous, true, retention);
    }

    public ScanManifest? LoadLatestManifest(string projectKey)
    {
        var latestId = Store.GetLatestSnapshotId(projectKey);
        return latestId is null ? null : Store.LoadManifest(projectKey, latestId);
    }

    public DiffEngine CreateDiffEngine(string projectKey) => new(Store.GetBlobStore(projectKey));

    public DiffResult Compare(
        string projectKey,
        ScanManifest? baseline,
        ScanManifest? target,
        DiffOptions? options = null) =>
        CreateDiffEngine(projectKey).Compare(baseline, target, options);
}
