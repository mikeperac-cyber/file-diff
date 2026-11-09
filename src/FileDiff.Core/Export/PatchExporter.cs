using System.Text;
using FileDiff.Core.Diffing;

namespace FileDiff.Core.Export;

/// <summary>
/// Writes the comparison as a unified diff in git's own format, so the result can be reviewed,
/// archived, or applied with <c>git apply</c>.
/// </summary>
public static class PatchExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Build(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();

        foreach (var change in request.Diff.Changes)
        {
            WriteEntry(builder, request, change);
        }

        return builder.ToString();
    }

    public static void WriteTo(ExportRequest request, string path) =>
        File.WriteAllText(path, Build(request), Utf8NoBom);

    private static void WriteEntry(StringBuilder builder, ExportRequest request, FileChange change)
    {
        var document = request.GetDocument(change);
        var oldPath = change.Kind == ChangeKind.Moved ? change.OldPath ?? change.Path : change.Path;
        var newPath = change.Path;

        builder.Append("diff --git a/").Append(oldPath).Append(" b/").Append(newPath).Append('\n');

        // A pure rename carries no content change, which git expresses as a rename header.
        if (change.Kind == ChangeKind.Moved && document.HasContent && document.Hunks.Count == 0)
        {
            builder.Append("similarity index 100%\n")
                .Append("rename from ").Append(oldPath).Append('\n')
                .Append("rename to ").Append(newPath).Append('\n');
            return;
        }

        if (!document.HasContent)
        {
            builder.Append("Binary files a/").Append(oldPath).Append(" and b/").Append(newPath).Append(" differ\n");
            return;
        }

        switch (change.Kind)
        {
            case ChangeKind.Added:
                builder.Append("new file mode 100644\n");
                break;

            case ChangeKind.Removed:
                builder.Append("deleted file mode 100644\n");
                break;
        }

        builder.Append("--- ")
            .Append(change.Kind == ChangeKind.Added ? "/dev/null" : "a/" + oldPath)
            .Append('\n');
        builder.Append("+++ ")
            .Append(change.Kind == ChangeKind.Removed ? "/dev/null" : "b/" + newPath)
            .Append('\n');

        foreach (var hunk in document.Hunks)
        {
            builder.Append(hunk.Header).Append('\n');

            foreach (var line in hunk.Lines)
            {
                builder.Append(line.Marker).Append(line.Text).Append('\n');
                AppendNoNewlineMarker(builder, document, line);
            }
        }
    }

    /// <summary>Git's way of saying a side's final line has no terminator.</summary>
    private static void AppendNoNewlineMarker(StringBuilder builder, FileDiffDocument document, DiffLine line)
    {
        if (line.Kind != DiffLineKind.Added
            && line.OldLineNumber == document.OldLineCount
            && !document.OldEndsWithNewline)
        {
            builder.Append("\\ No newline at end of file\n");
        }
        else if (line.Kind != DiffLineKind.Removed
                 && line.NewLineNumber == document.NewLineCount
                 && !document.NewEndsWithNewline)
        {
            builder.Append("\\ No newline at end of file\n");
        }
    }
}
