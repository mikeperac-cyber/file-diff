using FileDiff.Core.Diffing;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

public class DiffEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static BlobStore NewBlobStore(TempWorkspace workspace) =>
        new(Path.Combine(workspace.DataRoot, "blobs"));

    [Fact]
    public void Detects_added_removed_and_modified_files()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("keep.txt", "same"), ("edit.txt", "before"), ("gone.txt", "bye"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("keep.txt", "same"), ("edit.txt", "after"), ("new.txt", "hi"));

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.Equal(3, diff.Changes.Count);
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal("new.txt", diff.Changes.Single(c => c.Kind == ChangeKind.Added).Path);
        Assert.Equal("gone.txt", diff.Changes.Single(c => c.Kind == ChangeKind.Removed).Path);
        Assert.Equal("edit.txt", diff.Changes.Single(c => c.Kind == ChangeKind.Modified).Path);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Identical_content_reports_unchanged_even_when_the_timestamp_moved()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "identical content"));
        var after = TestManifests.Build(blobs, Start.AddHours(3), ("a.txt", "identical content"));

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.False(diff.HasChanges);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.Equal(1, diff.MetadataOnlyCount);
        Assert.Contains("No differences", diff.Summary());
    }

    [Fact]
    public void Same_size_edit_is_still_reported_as_modified()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "AAAA"));
        var after = TestManifests.Build(blobs, Start.AddSeconds(1), ("a.txt", "BBBB"));

        var change = Assert.Single(new DiffEngine(blobs).Compare(before, after).Changes);

        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.Equal(change.OldSize, change.NewSize);
        Assert.NotEqual(change.OldHash, change.NewHash);
    }

    [Fact]
    public void A_rename_is_reported_as_one_move_instead_of_a_delete_and_an_add()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("old/name.txt", "contents that move"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("new/name.txt", "contents that move"));

        var diff = new DiffEngine(blobs).Compare(before, after);
        var change = Assert.Single(diff.Changes);

        Assert.Equal(ChangeKind.Moved, change.Kind);
        Assert.Equal("new/name.txt", change.Path);
        Assert.Equal("old/name.txt", change.OldPath);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
    }

    [Fact]
    public void Move_plus_edit_is_reported_as_delete_and_add()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("old.txt", "original"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("new.txt", "original but edited"));

        var diff = new DiffEngine(blobs).Compare(before, after);

        // Content hashes no longer match, so the diff is honest about being an add plus a delete.
        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.MovedCount);
    }

    [Fact]
    public void Empty_files_are_never_paired_as_moves()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", string.Empty));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("b.txt", string.Empty));

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.MovedCount);
    }

    [Fact]
    public void Move_detection_can_be_switched_off()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("old.txt", "moves"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("new.txt", "moves"));

        var diff = new DiffEngine(blobs).Compare(before, after, new DiffOptions { DetectMoves = false });

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.MovedCount);
    }

    [Fact]
    public void Duplicate_content_pairs_up_one_for_one()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "dup"), ("b.txt", "dup"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("c.txt", "dup"), ("d.txt", "dup"));

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.Equal(2, diff.MovedCount);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
    }

    [Fact]
    public void Comparing_without_a_baseline_reports_everything_as_added()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);
        var after = TestManifests.Build(blobs, Start, ("a.txt", "one"), ("b.txt", "two"));

        var diff = new DiffEngine(blobs).Compare(null, after);

        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(2, diff.TargetFileCount);
    }

    [Fact]
    public void Comparing_without_a_target_reports_everything_as_removed()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);
        var before = TestManifests.Build(blobs, Start, ("a.txt", "one"));

        var diff = new DiffEngine(blobs).Compare(before, null);

        Assert.Equal(1, diff.RemovedCount);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Changes_are_ordered_by_path_for_stable_reading()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("z.txt", "1"), ("m.txt", "1"), ("a.txt", "1"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("z.txt", "2"), ("m.txt", "2"), ("a.txt", "2"));

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.Equal(["a.txt", "m.txt", "z.txt"], diff.Changes.Select(c => c.Path).ToArray());
    }

    [Fact]
    public void Unreadable_entries_are_treated_as_changed_rather_than_identical()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "content"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("a.txt", "content"));

        // Simulate "could not be read during this scan": no hash means identity cannot be proven.
        after.Files[0].Hash = string.Empty;
        after.Files[0].HasBlob = false;

        var diff = new DiffEngine(blobs).Compare(before, after);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Null(Assert.Single(diff.Changes).NewHash);
    }
}
