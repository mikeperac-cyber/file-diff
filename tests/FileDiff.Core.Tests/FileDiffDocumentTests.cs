using FileDiff.Core.Diffing;
using FileDiff.Core.Storage;

namespace FileDiff.Core.Tests;

/// <summary>Content-level diffing: what the user actually reads in the detail pane.</summary>
public class FileDiffDocumentTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static BlobStore NewBlobStore(TempWorkspace workspace) =>
        new(Path.Combine(workspace.DataRoot, "blobs"));

    [Fact]
    public void Modified_text_file_yields_hunks_and_line_counts()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("code.cs", "one\ntwo\nthree\nfour\nfive\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("code.cs", "one\ntwo\nTHREE\nfour\nfive\n"));

        var engine = new DiffEngine(blobs);
        var diff = engine.Compare(before, after);
        var document = engine.BuildFileDiff(Assert.Single(diff.Changes));

        Assert.True(document.HasContent);
        Assert.False(document.IsBinary);
        Assert.Equal(1, document.AddedLines);
        Assert.Equal(1, document.RemovedLines);
        Assert.Equal(5, document.OldLineCount);
        Assert.Equal(5, document.NewLineCount);

        var hunk = Assert.Single(document.Hunks);
        Assert.Contains(hunk.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "three");
        Assert.Contains(hunk.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "THREE");
    }

    [Fact]
    public void Compare_publishes_line_counts_for_the_file_list_preview()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "keep\ndrop\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("a.txt", "keep\ndrop\nadded\n"));

        var change = Assert.Single(new DiffEngine(blobs).Compare(before, after).Changes);

        Assert.Equal(1, change.AddedLines);
        Assert.Equal(0, change.RemovedLines);
    }

    [Fact]
    public void Line_counts_are_left_unknown_when_stats_are_switched_off()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "one\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("a.txt", "two\n"));

        var diff = new DiffEngine(blobs).Compare(before, after, new DiffOptions { ComputeLineStats = false });

        Assert.Null(Assert.Single(diff.Changes).AddedLines);
    }

    [Fact]
    public void Added_file_shows_every_line_as_new()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start);
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("new.txt", "alpha\nbeta\n"));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.True(document.HasContent);
        Assert.Equal(2, document.AddedLines);
        Assert.Equal(0, document.RemovedLines);
        Assert.Equal("@@ -0,0 +1,2 @@", Assert.Single(document.Hunks).Header);
    }

    [Fact]
    public void Deleted_file_shows_every_line_as_old()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("gone.txt", "alpha\nbeta\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.Equal(0, document.AddedLines);
        Assert.Equal(2, document.RemovedLines);
        Assert.Equal("@@ -1,2 +0,0 @@", Assert.Single(document.Hunks).Header);
    }

    [Fact]
    public void A_pure_move_has_no_content_changes()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("old.txt", "unchanged\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("new.txt", "unchanged\n"));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.True(document.HasContent);
        Assert.Empty(document.Hunks);
        Assert.Equal(0, document.AddedLines);
        Assert.Equal(0, document.RemovedLines);
    }

    [Fact]
    public void Line_ending_switch_is_reported_as_a_note_not_as_every_line_changing()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "alpha\r\nbeta\r\n"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("a.txt", "alpha\nbeta\n"));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.Empty(document.Hunks);
        Assert.Equal("CRLF", document.OldLineEnding);
        Assert.Equal("LF", document.NewLineEnding);
        Assert.Contains(document.Notes, note => note.Contains("Line endings changed", StringComparison.Ordinal));
    }

    [Fact]
    public void Trailing_newline_changes_are_noted()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start, ("a.txt", "alpha"));
        var after = TestManifests.Build(blobs, Start.AddMinutes(1), ("a.txt", "alpha\n"));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.Contains(document.Notes, note => note.Contains("Trailing newline added", StringComparison.Ordinal));
    }

    [Fact]
    public void Binary_content_is_reported_as_binary_instead_of_mojibake()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        var before = TestManifests.Build(blobs, Start);
        var after = TestManifests.Build(blobs, Start.AddMinutes(1));

        // Hand-write a blob containing a NUL byte so the sniffing rule is triggered.
        byte[] binary = [0x01, 0x00, 0x02, 0x03];
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(binary)).ToLowerInvariant();
        blobs.Write(hash, binary);
        after.Files.Add(new Models.FileEntry
        {
            RelativePath = "blob.bin",
            Size = binary.Length,
            LastWriteTimeUtc = Start,
            Hash = hash,
            IsText = false,
            HasBlob = true,
        });

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.True(document.IsBinary);
        Assert.False(document.HasContent);
        Assert.Contains("Binary", document.UnavailableReason);
        Assert.Empty(document.Hunks);
    }

    [Fact]
    public void Missing_content_explains_itself_instead_of_showing_an_empty_diff()
    {
        using var workspace = new TempWorkspace();
        var blobs = NewBlobStore(workspace);

        // Capture content is off, so hashes exist but no bytes were stored.
        var before = TestManifests.Build(null, Start, ("a.txt", "before"));
        var after = TestManifests.Build(null, Start.AddMinutes(1), ("a.txt", "after"));

        var engine = new DiffEngine(blobs);
        var document = engine.BuildFileDiff(Assert.Single(engine.Compare(before, after).Changes));

        Assert.False(document.HasContent);
        Assert.False(document.IsBinary);
        Assert.Contains("missing from the snapshot store", document.UnavailableReason);
    }
}
