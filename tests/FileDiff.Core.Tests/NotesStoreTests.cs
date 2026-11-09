using FileDiff.Core.Notes;

namespace FileDiff.Core.Tests;

public sealed class NotesStoreTests
{
    private static string NewDataRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileDiffNotesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Add_Toggle_Delete_Persist()
    {
        var root = NewDataRoot();
        try
        {
            var store = new NotesStore(root);
            var item = store.Add("Buy milk", new DateTime(2026, 10, 20));
            Assert.False(item.IsDone);

            var loaded = new NotesStore(root).Load();
            Assert.Single(loaded);
            Assert.Equal("Buy milk", loaded[0].Text);

            Assert.True(store.ToggleDone(item.Id));
            Assert.True(new NotesStore(root).Load()[0].IsDone);

            Assert.True(store.Delete(item.Id));
            Assert.Empty(new NotesStore(root).Load());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sort_Overdue_First_Done_Last()
    {
        var today = new DateTime(2026, 10, 15);
        var notes = new List<NoteItem>
        {
            new() { Text = "done", IsDone = true, DueLocal = today.AddDays(-5) },
            new() { Text = "nodate" },
            new() { Text = "future", DueLocal = today.AddDays(3) },
            new() { Text = "overdue", DueLocal = today.AddDays(-1) },
            new() { Text = "today", DueLocal = today },
        };

        var sorted = NotesStore.Sort(notes, today).Select(n => n.Text).ToList();
        Assert.Equal(["overdue", "today", "future", "nodate", "done"], sorted);
    }

    [Fact]
    public void Add_Rejects_Empty()
    {
        var store = new NotesStore(NewDataRoot());
        Assert.Throws<ArgumentException>(() => store.Add("  "));
    }
}
