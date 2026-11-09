using FileDiff.App.Infrastructure;
using FileDiff.Core.Diffing;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>Filtering the change list and loading the content of the selected file.</summary>
public sealed partial class MainViewModel
{
    private void ApplyFilter()
    {
        var query = _filterText.Trim();
        var filtered = _allChanges.Where(change => MatchesFilter(change, query)).ToList();

        _suspendDocumentLoad = true;

        Changes.Clear();

        foreach (var change in filtered)
        {
            Changes.Add(change);
        }

        _suspendDocumentLoad = false;

        SelectedChange = Changes.FirstOrDefault();

        if (SelectedChange is null)
        {
            ApplyDocument(null, null);
        }

        OnPropertyChanged(nameof(HasVisibleChanges));
        OnPropertyChanged(nameof(HasNoChanges));
        OnPropertyChanged(nameof(EverythingFilteredOut));
        OnPropertyChanged(nameof(ChangesHeader));
    }

    private bool MatchesFilter(FileChangeViewModel change, string query)
    {
        var kindAllowed = change.Kind switch
        {
            ChangeKind.Added => _showAdded,
            ChangeKind.Removed => _showRemoved,
            ChangeKind.Modified => _showModified,
            _ => _showMoved,
        };

        if (!kindAllowed)
        {
            return false;
        }

        return query.Length == 0
               || change.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (change.OldPath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task LoadDocumentAsync()
    {
        var change = _selectedChange;
        var engine = _diffEngine;
        var contextLines = ContextLines;

        if (change is null || engine is null)
        {
            ApplyDocument(null, null);
            return;
        }

        try
        {
            var document = await Task.Run(() => engine.BuildFileDiff(change.Change, contextLines));

            // The user may have moved on to another file while this was being read.
            if (!ReferenceEquals(_selectedChange, change))
            {
                return;
            }

            ApplyDocument(document, change);
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not build the diff for '{change.Path}': {exception.Message}");
        }
    }

    private void ApplyDocument(FileDiffDocument? document, FileChangeViewModel? change)
    {
        DiffLines.Clear();
        DocumentNotes.Clear();

        if (document is null || change is null)
        {
            DocumentHeader = "Select a changed file to see what changed in it";
            DocumentSubHeader = string.Empty;
            DocumentUnavailableReason = null;
            DocumentIsBinary = false;
            DocumentTooLarge = false;
            OnPropertyChanged(nameof(DocumentHasLines));
            OnPropertyChanged(nameof(ShowNoContentMessage));
            return;
        }

        // Line counts may only be known now: big comparisons skip the up-front statistics pass.
        if (document.HasContent)
        {
            change.Change.AddedLines = document.AddedLines;
            change.Change.RemovedLines = document.RemovedLines;
            change.RefreshLineCounts();
        }

        DocumentHeader = change.Path;
        DocumentSubHeader = BuildDocumentSubHeader(document, change);
        DocumentIsBinary = document.IsBinary;
        DocumentTooLarge = document.TooLarge;
        DocumentUnavailableReason = document.UnavailableReason;

        foreach (var note in document.Notes)
        {
            DocumentNotes.Add(note);
        }

        foreach (var hunk in document.Hunks)
        {
            DiffLines.Add(DiffLineViewModel.HunkHeader(hunk.Header));

            foreach (var line in hunk.Lines)
            {
                DiffLines.Add(new DiffLineViewModel(line));
            }
        }

        OnPropertyChanged(nameof(DocumentHasLines));
        OnPropertyChanged(nameof(ShowNoContentMessage));
    }

    private static string BuildDocumentSubHeader(FileDiffDocument document, FileChangeViewModel change)
    {
        var parts = new List<string> { change.KindLabel, change.SizeLabel };

        if (document.HasContent)
        {
            parts.Add($"+{document.AddedLines} −{document.RemovedLines} lines");
            parts.Add($"{document.OldLineCount} → {document.NewLineCount} lines");

            if (!string.IsNullOrEmpty(document.NewEncoding))
            {
                parts.Add(document.NewEncoding!);
            }

            if (!string.IsNullOrEmpty(document.NewLineEnding))
            {
                parts.Add(document.NewLineEnding!);
            }
        }

        if (change.OldPath is { Length: > 0 } oldPath)
        {
            parts.Add($"moved from {oldPath}");
        }

        return string.Join("  ·  ", parts);
    }
}
