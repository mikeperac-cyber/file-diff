using FileDiff.App.Infrastructure;
using FileDiff.Core.Notes;

namespace FileDiff.App.ViewModels;

/// <summary>One row in the notes card.</summary>
public sealed class NoteItemViewModel : ObservableObject
{
    public NoteItemViewModel(NoteItem item) => Item = item;

    public NoteItem Item { get; }

    public string Id => Item.Id;

    public string Text => Item.Text;

    public bool IsDone => Item.IsDone;

    public string DueLabel
    {
        get
        {
            if (!Item.DueLocal.HasValue)
            {
                return string.Empty;
            }

            var due = Item.DueLocal.Value.Date;
            var today = DateTime.Today;
            var diff = (due - today).Days;
            var date = due.ToString("MMM d");
            return diff switch
            {
                < 0 => Item.IsDone ? $"was due {date}" : $"overdue · {date}",
                0 => "due today",
                1 => $"due tomorrow · {date}",
                _ => $"due {date}",
            };
        }
    }

    public bool IsOverdue => Item.IsOverdue(DateTime.Now);

    public bool IsDueToday => Item.IsDueToday(DateTime.Now);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(DueLabel));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(IsDueToday));
    }
}
