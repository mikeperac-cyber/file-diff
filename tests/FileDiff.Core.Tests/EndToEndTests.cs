using System.Text;
using FileDiff.Core.Diffing;
using FileDiff.Core.Export;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

/// <summary>
/// The real thing: point the app at a folder, save a snapshot, change the folder, take another
/// snapshot, and read the differences back — including the exported reports.
/// </summary>
public class EndToEndTests
{
    private static SnapshotService NewService(TempWorkspace workspace) =>
        new(new SnapshotStore(workspace.DataRoot));

    [Fact]
    public async Task Full_workflow_detects_every_kind_of_change()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);
        var options = workspace.NewScanOptions();

        // ----- first visit to the folder -----
        workspace.Write("src/Program.cs", "class Program\n{\n    static void Main() {}\n}\n");
        workspace.Write("README.md", "# Project\n\nHello.\n");
        workspace.Write("keep/same.txt", "identical\n");
        workspace.Write("old-name.txt", "this file will be renamed\n");

        var first = await service.CaptureAsync(options, saveSnapshot: true);

        Assert.True(first.Saved);
        Assert.Null(first.Previous);
        Assert.Equal(4, first.Manifest.FileCount);
        Assert.Empty(first.Manifest.Warnings);

        // ----- the user edits the folder -----
        workspace.Write("src/Program.cs", "class Program\n{\n    static void Main() => Run();\n}\n");
        workspace.Write("README.md", "# Project\n\nHello, there.\n");
        workspace.Write("docs/guide.md", "# Guide\n");
        workspace.Delete("keep/same.txt");
        workspace.Move("old-name.txt", "new-name.txt");

        var second = await service.CaptureAsync(options, saveSnapshot: true);
        var projectKey = second.Project.ProjectKey;

        Assert.NotNull(second.Previous);
        Assert.Equal(first.Manifest.SnapshotId, second.Previous!.SnapshotId);

        // ----- compare -----
        var diff = service.Compare(projectKey, first.Manifest, second.Manifest);

        Assert.Equal(5, diff.Changes.Count);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(2, diff.ModifiedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.MovedCount);
        Assert.Equal(0, diff.UnchangedCount);

        Assert.Equal("docs/guide.md", diff.Changes.Single(c => c.Kind == ChangeKind.Added).Path);
        Assert.Equal("keep/same.txt", diff.Changes.Single(c => c.Kind == ChangeKind.Removed).Path);

        var moved = diff.Changes.Single(c => c.Kind == ChangeKind.Moved);
        Assert.Equal("new-name.txt", moved.Path);
        Assert.Equal("old-name.txt", moved.OldPath);

        // ----- content of a changed file -----
        var engine = service.CreateDiffEngine(projectKey);
        var program = engine.BuildFileDiff(diff.Changes.Single(c => c.Path == "src/Program.cs"));

        Assert.True(program.HasContent);
        Assert.Equal(1, program.AddedLines);
        Assert.Equal(1, program.RemovedLines);

        var movedDocument = engine.BuildFileDiff(moved);
        Assert.Empty(movedDocument.Hunks);

        // ----- exports -----
        var exporter = new ExportRequest
        {
            Diff = diff,
            GetDocument = change => engine.BuildFileDiff(change),
            BaselineLabel = first.Manifest.SnapshotId,
            TargetLabel = second.Manifest.SnapshotId,
        };

        var patch = PatchExporter.Build(exporter);
        var html = HtmlExporter.Build(exporter);
        var csv = CsvExporter.Build(exporter);

        Assert.Contains("diff --git a/src/Program.cs b/src/Program.cs", patch);
        Assert.Contains("+    static void Main() => Run();", patch);
        Assert.Contains("rename from old-name.txt", patch);
        Assert.Contains("rename to new-name.txt", patch);
        Assert.Contains("new file mode 100644", patch);
        Assert.Contains("deleted file mode 100644", patch);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("docs/guide.md", html);
        Assert.Contains("src/Program.cs", html);

        Assert.Contains("Path,Change,PreviousPath", csv);
        Assert.Equal(6, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length); // header + 5 rows

        // ----- timeline and storage -----
        Assert.Equal(2, service.Store.GetSnapshotCount(projectKey));
        Assert.True(service.Store.GetStoredBytes(projectKey) > 0);
    }

    [Fact]
    public async Task Rescanning_an_untouched_folder_reports_no_differences()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);
        var options = workspace.NewScanOptions();

        workspace.Write("a.txt", "alpha\n");
        workspace.Write("b/c.txt", "beta\n");

        var first = await service.CaptureAsync(options, saveSnapshot: true);
        var second = await service.CaptureAsync(options, saveSnapshot: true);
        var projectKey = second.Project.ProjectKey;

        var diff = service.Compare(projectKey, first.Manifest, second.Manifest);

        Assert.False(diff.HasChanges);
        Assert.Equal(2, diff.UnchangedCount);
        Assert.Contains("No differences", diff.Summary());

        // Nothing changed, so nothing was captured twice.
        Assert.Equal(0, second.Manifest.BlobBytes);
    }

    [Fact]
    public async Task Live_check_detects_changes_without_touching_the_timeline()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);
        var options = workspace.NewScanOptions();

        workspace.Write("a.txt", "before\n");
        var saved = await service.CaptureAsync(options, saveSnapshot: true);
        var projectKey = saved.Project.ProjectKey;

        workspace.Write("a.txt", "after\n");

        var live = await service.CaptureAsync(options, saveSnapshot: false);

        Assert.True(live.IsLiveScan);
        Assert.Empty(live.Manifest.SnapshotId);
        Assert.Equal(1, service.Store.GetSnapshotCount(projectKey));

        var diff = service.Compare(projectKey, saved.Manifest, live.Manifest);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal("a.txt", Assert.Single(diff.Changes).Path);
    }

    [Fact]
    public async Task Any_two_snapshots_can_be_compared_not_just_neighbours()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);
        var options = workspace.NewScanOptions();

        workspace.Write("version.txt", "one\n");
        var first = await service.CaptureAsync(options, saveSnapshot: true);
        var projectKey = first.Project.ProjectKey;

        workspace.Write("version.txt", "two\n");
        await service.CaptureAsync(options, saveSnapshot: true);

        workspace.Write("version.txt", "three\n");
        var third = await service.CaptureAsync(options, saveSnapshot: true);

        var diff = service.Compare(projectKey, first.Manifest, third.Manifest);
        var engine = service.CreateDiffEngine(projectKey);
        var document = engine.BuildFileDiff(Assert.Single(diff.Changes));

        Assert.Contains(document.Hunks, hunk => hunk.Lines.Any(l => l.Kind == DiffLineKind.Removed && l.Text == "one"));
        Assert.Contains(document.Hunks, hunk => hunk.Lines.Any(l => l.Kind == DiffLineKind.Added && l.Text == "three"));
    }

    [Fact]
    public async Task Retention_prunes_the_timeline_but_keeps_recent_content_readable()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);

        var options = workspace.NewScanOptions();
        options.MaxSnapshotsToKeep = 2;

        workspace.Write("a.txt", "version one\n");
        await service.CaptureAsync(options, saveSnapshot: true);

        workspace.Write("a.txt", "version two\n");
        var second = await service.CaptureAsync(options, saveSnapshot: true);

        workspace.Write("a.txt", "version three\n");
        var third = await service.CaptureAsync(options, saveSnapshot: true);

        var projectKey = third.Project.ProjectKey;
        var timeline = service.Store.LoadTimeline(projectKey);

        Assert.Equal(2, timeline.Count);
        Assert.DoesNotContain(timeline, s => s.SnapshotId == second.Previous!.SnapshotId);

        // The newest comparison still has readable content on both sides.
        var diff = service.Compare(projectKey, second.Manifest, third.Manifest);
        var engine = service.CreateDiffEngine(projectKey);
        var document = engine.BuildFileDiff(Assert.Single(diff.Changes));

        Assert.True(document.HasContent);
        Assert.Equal(1, document.AddedLines);
        Assert.Equal(1, document.RemovedLines);
    }

    [Fact]
    public async Task The_app_refuses_to_watch_its_own_storage_folder()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);

        var options = new FileDiff.Core.Models.ScanOptions { RootPath = workspace.DataRoot };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CaptureAsync(options, saveSnapshot: true));

        Assert.Contains("stores its own snapshots", error.Message);
    }

    [Fact]
    public async Task Exports_can_be_written_to_disk_and_read_back()
    {
        using var workspace = new TempWorkspace();
        var service = NewService(workspace);
        var options = workspace.NewScanOptions();

        workspace.Write("a.txt", "before\n");
        var first = await service.CaptureAsync(options, saveSnapshot: true);
        workspace.Write("a.txt", "after\n");
        var second = await service.CaptureAsync(options, saveSnapshot: true);

        var engine = service.CreateDiffEngine(second.Project.ProjectKey);
        var request = new ExportRequest
        {
            Diff = service.Compare(second.Project.ProjectKey, first.Manifest, second.Manifest),
            GetDocument = change => engine.BuildFileDiff(change),
        };

        var patchPath = Path.Combine(workspace.DataRoot, "report.patch");
        var htmlPath = Path.Combine(workspace.DataRoot, "report.html");
        var csvPath = Path.Combine(workspace.DataRoot, "report.csv");

        PatchExporter.WriteTo(request, patchPath);
        HtmlExporter.WriteTo(request, htmlPath);
        CsvExporter.WriteTo(request, csvPath);

        Assert.Contains("-before", await File.ReadAllTextAsync(patchPath));
        Assert.Contains("<table class=\"diff\">", await File.ReadAllTextAsync(htmlPath));

        var csv = await File.ReadAllTextAsync(csvPath);
        Assert.Contains("a.txt", csv);
        Assert.Contains("Modified", csv);
    }
}
