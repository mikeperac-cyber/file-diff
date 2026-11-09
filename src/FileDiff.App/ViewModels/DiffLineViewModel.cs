using FileDiff.Core.Diffing;

namespace FileDiff.App.ViewModels;

/// <summary>
/// One rendered row of the diff: either a hunk header or a single line with its old/new numbers.
/// </summary>
public sealed class DiffLineViewModel
{
    private DiffLineViewModel(string header)
    {
        IsHunkHeader = true;
        Text = header;
        Header = header;
        Kind = DiffLineKind.Context;
    }

    public DiffLineViewModel(DiffLine line)
    {
        Kind = line.Kind;
        Text = line.Text;
        Prefix = line.Prefix;
        Emphasis = line.Emphasis;
        Suffix = line.Suffix;
        HasEmphasis = line.HasEmphasis;
        Marker = line.Marker;
        OldNumber = line.OldLineNumber?.ToString() ?? string.Empty;
        NewNumber = line.NewLineNumber?.ToString() ?? string.Empty;
    }

    public static DiffLineViewModel HunkHeader(string header) => new(header);

    public bool IsHunkHeader { get; }

    public string Header { get; } = string.Empty;

    public DiffLineKind Kind { get; }

    public string Text { get; }

    public string Prefix { get; } = string.Empty;

    public string Emphasis { get; } = string.Empty;

    public string Suffix { get; } = string.Empty;

    public bool HasEmphasis { get; }

    public string Marker { get; } = string.Empty;

    public string OldNumber { get; } = string.Empty;

    public string NewNumber { get; } = string.Empty;
}
