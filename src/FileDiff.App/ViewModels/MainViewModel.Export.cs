using System.IO;
using System.Text;
using FileDiff.App.Infrastructure;
using FileDiff.Core.Export;

namespace FileDiff.App.ViewModels;

/// <summary>Writing the comparison out as a patch, an HTML report, or a CSV.</summary>
public sealed partial class MainViewModel
{
    private async Task ExportAsync(ExportFormat format)
    {
        var diff = _currentDiff;
        var engine = _diffEngine;

        if (diff is null || engine is null || !diff.HasChanges)
        {
            return;
        }

        var (extension, filter) = format switch
        {
            ExportFormat.Patch => (".patch", "Patch file (*.patch)|*.patch|All files (*.*)|*.*"),
            ExportFormat.Html => (".html", "HTML report (*.html)|*.html|All files (*.*)|*.*"),
            _ => (".csv", "CSV file (*.csv)|*.csv|All files (*.*)|*.*"),
        };

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export the comparison",
            FileName = $"filediff-{SanitizeForFileName(_selectedSnapshot?.SnapshotId ?? "comparison")}{extension}",
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FileName;
        var contextLines = ContextLines;
        var isLiveTarget = _selectedSnapshot?.IsLive == true;

        var request = new ExportRequest
        {
            Diff = diff,
            GetDocument = change => engine.BuildFileDiff(change, contextLines),
            BaselineLabel = _baselineSnapshot?.TimeLabel ?? "baseline",
            TargetLabel = DescribeTarget(),
            Note = isLiveTarget
                ? "The target of this comparison was a live scan that was not saved as a snapshot."
                : null,
        };

        try
        {
            IsBusy = true;
            StatusText = "Exporting…";

            await Task.Run(() =>
            {
                switch (format)
                {
                    case ExportFormat.Patch:
                        PatchExporter.WriteTo(request, path);
                        break;

                    case ExportFormat.Html:
                        HtmlExporter.WriteTo(request, path);
                        break;

                    default:
                        CsvExporter.WriteTo(request, path);
                        break;
                }
            });

            StatusText = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '-' : character);
        }

        return builder.ToString();
    }
}
