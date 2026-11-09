using System.Collections.ObjectModel;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Diffing;

namespace FileDiff.App.ViewModels;

internal enum ExportFormat
{
    Patch,
    Html,
    Csv,
}

/// <summary>State of the comparison view: filters, summary counts and the selected file.</summary>
public sealed partial class MainViewModel
{
    private FileChangeViewModel? _selectedChange;
    private string _filterText = string.Empty;
    private bool _showAdded = true;
    private bool _showRemoved = true;
    private bool _showModified = true;
    private bool _showMoved = true;
    private string _compareHeader = "No snapshot selected";
    private string _summaryLine = string.Empty;
    private bool _hasDiff;
    private int _addedCount;
    private int _removedCount;
    private int _modifiedCount;
    private int _movedCount;
    private int _unchangedCount;
    private string _documentHeader = "Select a changed file to see what changed in it";
    private string _documentSubHeader = string.Empty;
    private string? _documentUnavailableReason;
    private bool _documentIsBinary;
    private bool _documentTooLarge;

    public ObservableCollection<string> DocumentNotes { get; } = [];

    /// <summary>The changed file whose content is shown in the diff pane.</summary>
    public FileChangeViewModel? SelectedChange
    {
        get => _selectedChange;
        set
        {
            if (!SetProperty(ref _selectedChange, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedFilePath));
            RefreshCommands();

            if (!_suspendDocumentLoad)
            {
                _ = LoadDocumentAsync();
            }
        }
    }

    public string SelectedFilePath => _selectedChange?.Path ?? string.Empty;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowAdded
    {
        get => _showAdded;
        set
        {
            if (SetProperty(ref _showAdded, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowRemoved
    {
        get => _showRemoved;
        set
        {
            if (SetProperty(ref _showRemoved, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowModified
    {
        get => _showModified;
        set
        {
            if (SetProperty(ref _showModified, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowMoved
    {
        get => _showMoved;
        set
        {
            if (SetProperty(ref _showMoved, value))
            {
                ApplyFilter();
            }
        }
    }

    public string CompareHeader => _compareHeader;

    public string SummaryLine => _summaryLine;

    public bool HasDiff => _hasDiff;

    public int AddedCount => _addedCount;

    public int RemovedCount => _removedCount;

    public int ModifiedCount => _modifiedCount;

    public int MovedCount => _movedCount;

    public int UnchangedCount => _unchangedCount;

    public bool HasVisibleChanges => Changes.Count > 0;

    public bool HasNoChanges => _hasDiff && _allChanges.Count == 0;

    public bool EverythingFilteredOut => _hasDiff && _allChanges.Count > 0 && Changes.Count == 0;

    public bool HasComparableDiff => _currentDiff is { HasChanges: true };

    public string ChangesHeader
    {
        get
        {
            if (!_hasDiff)
            {
                return "No comparison yet";
            }

            if (_allChanges.Count == 0)
            {
                return "No changes";
            }

            return Changes.Count == _allChanges.Count
                ? $"{Changes.Count} changed file{(Changes.Count == 1 ? string.Empty : "s")}"
                : $"{Changes.Count} of {_allChanges.Count} changed files shown";
        }
    }

    public string DocumentHeader
    {
        get => _documentHeader;
        private set => SetProperty(ref _documentHeader, value);
    }

    public string DocumentSubHeader
    {
        get => _documentSubHeader;
        private set => SetProperty(ref _documentSubHeader, value);
    }

    public string? DocumentUnavailableReason
    {
        get => _documentUnavailableReason;
        private set
        {
            if (SetProperty(ref _documentUnavailableReason, value))
            {
                OnPropertyChanged(nameof(HasDocumentNote));
            }
        }
    }

    public bool DocumentIsBinary
    {
        get => _documentIsBinary;
        private set => SetProperty(ref _documentIsBinary, value);
    }

    public bool DocumentTooLarge
    {
        get => _documentTooLarge;
        private set => SetProperty(ref _documentTooLarge, value);
    }

    public bool HasDocumentNote => !string.IsNullOrEmpty(_documentUnavailableReason);

    public bool DocumentHasLines => DiffLines.Count > 0;

    /// <summary>An entry is selected but there is nothing in it to render (e.g. a pure rename).</summary>
    public bool ShowNoContentMessage =>
        _selectedChange is not null && DiffLines.Count == 0 && !HasDocumentNote;
}
