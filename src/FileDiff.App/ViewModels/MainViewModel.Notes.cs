using System.Collections.ObjectModel;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Notes;

namespace FileDiff.App.ViewModels;

/// <summary>Sidebar notes card: quick notes, to-dos (checkbox) and reminders (due date).</summary>
public sealed partial class MainViewModel
{
    private readonly NotesStore _notes = new();
    private string _newNoteText = string.Empty;
    private DateTime? _newNoteDueDate;
    private NoteItemViewModel? _selectedNote;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = [];

    public string NewNoteText
    {
        get => _newNoteText;
        set
        {
            if (SetProperty(ref _newNoteText, value ?? string.Empty))
            {
                RefreshCommands();
            }
        }
    }

    public DateTime? NewNoteDueDate
    {
        get => _newNoteDueDate;
        set => SetProperty(ref _newNoteDueDate, value);
    }

    public NoteItemViewModel? SelectedNote
    {
        get => _selectedNote;
        set => SetProperty(ref _selectedNote, value);
    }

    public int NotesOpenCount => Notes.Count(n => !n.IsDone);

    public int NotesOverdueCount => Notes.Count(n => n.IsOverdue);

    public string NotesHeader => NotesOverdueCount > 0
        ? $"NOTES · {NotesOverdueCount} overdue"
        : NotesOpenCount > 0
            ? $"NOTES · {NotesOpenCount} open"
            : "NOTES";

    public RelayCommand AddNoteCommand { get; private set; } = null!;

    public RelayCommand ToggleNoteCommand { get; private set; } = null!;

    public RelayCommand DeleteNoteCommand { get; private set; } = null!;

    public RelayCommand ClearDoneNotesCommand { get; private set; } = null!;

    private void RegisterNotesCommands()
    {
        AddNoteCommand = Track(new RelayCommand(AddNote, () => !string.IsNullOrWhiteSpace(_newNoteText)));
        ToggleNoteCommand = Track(new RelayCommand(
            () => ToggleSelectedNote(),
            () => SelectedNote is not null));
        DeleteNoteCommand = Track(new RelayCommand(
            () => DeleteSelectedNote(),
            () => SelectedNote is not null));
        ClearDoneNotesCommand = Track(new RelayCommand(
            () => ClearDoneNotes(),
            () => Notes.Any(n => n.IsDone)));
        LoadNotes();
    }

    private void LoadNotes()
    {
        Notes.Clear();
        foreach (var item in NotesStore.Sort(_notes.Load(), DateTime.Now))
        {
            Notes.Add(new NoteItemViewModel(item));
        }

        OnPropertyChanged(nameof(NotesOpenCount));
        OnPropertyChanged(nameof(NotesOverdueCount));
        OnPropertyChanged(nameof(NotesHeader));
        RefreshCommands();
    }

    private void AddNote()
    {
        var text = _newNoteText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            _notes.Add(text, _newNoteDueDate);
            NewNoteText = string.Empty;
            NewNoteDueDate = null;
            OnPropertyChanged(nameof(NewNoteText));
            OnPropertyChanged(nameof(NewNoteDueDate));
            LoadNotes();
            StatusText = "Note added.";
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void ToggleSelectedNote()
    {
        if (SelectedNote is null)
        {
            return;
        }

        _notes.ToggleDone(SelectedNote.Id);
        LoadNotes();
    }

    private void DeleteSelectedNote()
    {
        if (SelectedNote is null)
        {
            return;
        }

        _notes.Delete(SelectedNote.Id);
        SelectedNote = null;
        LoadNotes();
    }

    private void ClearDoneNotes()
    {
        var removed = _notes.ClearCompleted();
        SelectedNote = null;
        LoadNotes();
        StatusText = removed == 0 ? "No completed notes to clear." : $"Cleared {removed} completed note(s).";
    }
}
