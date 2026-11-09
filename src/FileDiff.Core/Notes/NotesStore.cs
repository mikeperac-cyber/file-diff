using FileDiff.Core.Storage;

namespace FileDiff.Core.Notes;

/// <summary>Global sidebar notes, persisted to <c>%APPDATA%\FileDiff\notes.json</c>.</summary>
public sealed class NotesStore
{
    private readonly string _filePath;
    private readonly object _gate = new();

    public NotesStore(string? dataRoot = null)
    {
        _filePath = Path.Combine(
            string.IsNullOrWhiteSpace(dataRoot) ? AppPaths.DataRoot : dataRoot,
            "notes.json");
    }

    public string FilePath => _filePath;

    public List<NoteItem> Load()
    {
        lock (_gate)
        {
            return JsonFile.TryRead<List<NoteItem>>(_filePath) ?? [];
        }
    }

    public void Save(IEnumerable<NoteItem> notes)
    {
        lock (_gate)
        {
            JsonFile.WriteAtomic(_filePath, notes.ToList());
        }
    }

    public NoteItem Add(string text, DateTime? dueLocal = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Note text is empty.", nameof(text));
        }

        lock (_gate)
        {
            var notes = JsonFile.TryRead<List<NoteItem>>(_filePath) ?? [];
            var item = new NoteItem { Text = text.Trim(), DueLocal = dueLocal?.Date };
            notes.Insert(0, item);
            JsonFile.WriteAtomic(_filePath, notes);
            return item;
        }
    }

    public bool ToggleDone(string id)
    {
        lock (_gate)
        {
            var notes = JsonFile.TryRead<List<NoteItem>>(_filePath) ?? [];
            var item = notes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
            if (item is null)
            {
                return false;
            }

            item.IsDone = !item.IsDone;
            JsonFile.WriteAtomic(_filePath, notes);
            return item.IsDone;
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var notes = JsonFile.TryRead<List<NoteItem>>(_filePath) ?? [];
            var removed = notes.RemoveAll(n => string.Equals(n.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                JsonFile.WriteAtomic(_filePath, notes);
            }

            return removed;
        }
    }

    public int ClearCompleted()
    {
        lock (_gate)
        {
            var notes = JsonFile.TryRead<List<NoteItem>>(_filePath) ?? [];
            var removed = notes.RemoveAll(n => n.IsDone);
            if (removed > 0)
            {
                JsonFile.WriteAtomic(_filePath, notes);
            }

            return removed;
        }
    }

    /// <summary>Overdue first, then due today, then dated, then undated; done items last.</summary>
    public static List<NoteItem> Sort(IEnumerable<NoteItem> notes, DateTime nowLocal)
    {
        return notes.OrderBy(n => n.IsDone)
            .ThenBy(n => n.DueLocal.HasValue ? 0 : 1)
            .ThenBy(n => n.DueLocal ?? DateTime.MaxValue)
            .ThenByDescending(n => n.CreatedAtUtc)
            .ToList();
    }
}
