using System.Globalization;
using System.Text;
using FileDiff.Core.Diffing;

namespace FileDiff.Core.Export;

/// <summary>Flat change list, suitable for a spreadsheet or for post-processing.</summary>
public static class CsvExporter
{
    private static readonly string[] Headers =
    [
        "Path",
        "Change",
        "PreviousPath",
        "SizeBefore",
        "SizeAfter",
        "LinesAdded",
        "LinesRemoved",
        "HashBefore",
        "HashAfter",
        "ModifiedBeforeUtc",
        "ModifiedAfterUtc",
    ];

    public static string Build(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        builder.Append(string.Join(',', Headers)).Append("\r\n");

        foreach (var change in request.Diff.Changes)
        {
            builder
                .Append(Quote(change.Path)).Append(',')
                .Append(Quote(change.KindLabel)).Append(',')
                .Append(Quote(change.OldPath)).Append(',')
                .Append(change.OldSize).Append(',')
                .Append(change.NewSize).Append(',')
                .Append(FormatNumber(change.AddedLines)).Append(',')
                .Append(FormatNumber(change.RemovedLines)).Append(',')
                .Append(Quote(change.OldHash)).Append(',')
                .Append(Quote(change.NewHash)).Append(',')
                .Append(Quote(FormatTime(change.OldModifiedUtc))).Append(',')
                .Append(Quote(FormatTime(change.NewModifiedUtc)))
                .Append("\r\n");
        }

        return builder.ToString();
    }

    public static void WriteTo(ExportRequest request, string path) =>
        File.WriteAllText(path, Build(request), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string FormatNumber(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>RFC 4180 quoting.</summary>
    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
