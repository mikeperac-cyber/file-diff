using System.Globalization;
using FileDiff.Core.Util;

namespace FileDiff.App.ViewModels;

/// <summary>
/// Preferences. Numeric settings are edited as text so typing anything invalid gets corrected
/// to a valid value instead of failing silently, and every change is persisted immediately.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _captureContent = true;
    private bool _includeHiddenFiles;
    private bool _settingsExpanded;
    private string _contextLinesText = "3";
    private string _keepSnapshotsText = "50";
    private string _maxContentMegabytesText = "5";
    private string _ignorePatternsText = string.Empty;

    public bool CaptureContent
    {
        get => _captureContent;
        set
        {
            if (SetProperty(ref _captureContent, value))
            {
                _settings.CaptureContent = value;
                Persist();
            }
        }
    }

    public bool IncludeHiddenFiles
    {
        get => _includeHiddenFiles;
        set
        {
            if (SetProperty(ref _includeHiddenFiles, value))
            {
                _settings.IncludeHiddenFiles = value;
                Persist();
            }
        }
    }

    /// <summary>Context lines around each change in the diff pane.</summary>
    public int ContextLines => _settings.ContextLines;

    /// <summary>Whether the preferences panel in the sidebar is open.</summary>
    public bool SettingsExpanded
    {
        get => _settingsExpanded;
        set => SetProperty(ref _settingsExpanded, value);
    }

    public string ContextLinesText
    {
        get => _contextLinesText;
        set
        {
            if (!SetProperty(ref _contextLinesText, value))
            {
                return;
            }

            var parsed = ParseIntOr(value, _settings.ContextLines, 0, 50);
            _settings.ContextLines = parsed;
            PushNormalized(nameof(ContextLinesText), ref _contextLinesText, parsed);
            Persist();

            OnPropertyChanged(nameof(ContextLines));
            _ = RecomputeDiffAsync();
        }
    }

    public string KeepSnapshotsText
    {
        get => _keepSnapshotsText;
        set
        {
            if (!SetProperty(ref _keepSnapshotsText, value))
            {
                return;
            }

            var parsed = ParseIntOr(value, _settings.MaxSnapshotsToKeep, 1, 5000);
            _settings.MaxSnapshotsToKeep = parsed;
            PushNormalized(nameof(KeepSnapshotsText), ref _keepSnapshotsText, parsed);
            Persist();
        }
    }

    /// <summary>Content up to this many megabytes is captured so it can be diffed line by line.</summary>
    public int MaxContentMegabytes => (int)Math.Clamp(_settings.MaxTextFileBytes / (1024d * 1024d), 1, 512);

    public string MaxContentMegabytesText
    {
        get => _maxContentMegabytesText;
        set
        {
            if (!SetProperty(ref _maxContentMegabytesText, value))
            {
                return;
            }

            var parsed = ParseIntOr(value, MaxContentMegabytes, 1, 512);
            _settings.MaxTextFileBytes = parsed * 1024L * 1024L;
            PushNormalized(nameof(MaxContentMegabytesText), ref _maxContentMegabytesText, parsed);
            Persist();

            OnPropertyChanged(nameof(MaxContentMegabytes));
        }
    }

    public string IgnorePatternsText
    {
        get => _ignorePatternsText;
        set
        {
            if (!SetProperty(ref _ignorePatternsText, value))
            {
                return;
            }

            _settings.IgnorePatterns = [.. ParseIgnorePatterns()];
            OnPropertyChanged(nameof(IgnoreRuleCountLabel));
            Persist();
        }
    }

    public string IgnoreRuleCountLabel
    {
        get
        {
            var extra = ParseIgnorePatterns().Count();
            return extra == 0
                ? "No extra rules — the built-in defaults (build output, source control, temp files) always apply."
                : $"{extra} extra rule{(extra == 1 ? string.Empty : "s")} on top of the defaults. Applies to the next snapshot.";
        }
    }

    private IEnumerable<string> ParseIgnorePatterns() =>
        (_ignorePatternsText ?? string.Empty)
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

    private void PushNormalized(string propertyName, ref string field, int value)
    {
        var normalized = value.ToString(CultureInfo.InvariantCulture);

        if (!string.Equals(field, normalized, StringComparison.Ordinal))
        {
            field = normalized;
            OnPropertyChanged(propertyName);
        }
    }

    private static int ParseIntOr(string? text, int fallback, int minimum, int maximum) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : Math.Clamp(fallback, minimum, maximum);

    private void LoadSettingsIntoUi()
    {
        _loadingSettings = true;

        try
        {
            _captureContent = _settings.CaptureContent;
            _includeHiddenFiles = _settings.IncludeHiddenFiles;
            _contextLinesText = _settings.ContextLines.ToString(CultureInfo.InvariantCulture);
            _keepSnapshotsText = _settings.MaxSnapshotsToKeep.ToString(CultureInfo.InvariantCulture);
            _maxContentMegabytesText = MaxContentMegabytes.ToString(CultureInfo.InvariantCulture);
            _ignorePatternsText = string.Join(Environment.NewLine, _settings.IgnorePatterns);

            OnPropertyChanged(nameof(CaptureContent));
            OnPropertyChanged(nameof(IncludeHiddenFiles));
            OnPropertyChanged(nameof(ContextLines));
            OnPropertyChanged(nameof(ContextLinesText));
            OnPropertyChanged(nameof(KeepSnapshotsText));
            OnPropertyChanged(nameof(MaxContentMegabytes));
            OnPropertyChanged(nameof(MaxContentMegabytesText));
            OnPropertyChanged(nameof(IgnorePatternsText));
            OnPropertyChanged(nameof(IgnoreRuleCountLabel));
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void Persist()
    {
        if (_loadingSettings)
        {
            return;
        }

        try
        {
            _settings.Save();
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not save settings: {exception.Message}");
        }
    }
}
