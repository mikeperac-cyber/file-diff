using FileDiff.App.Infrastructure;

namespace FileDiff.App.ViewModels;

/// <summary>Light/Dark theme switch. Persisted in settings; applied via ThemeManager.</summary>
public sealed partial class MainViewModel
{
    public string AppTheme => _settings.Theme;

    public bool IsLightTheme => !string.Equals(_settings.Theme, ThemeManager.Dark, StringComparison.OrdinalIgnoreCase);

    public RelayCommand ToggleThemeCommand { get; private set; } = null!;

    private void RegisterThemeCommand()
    {
        ToggleThemeCommand = Track(new RelayCommand(ToggleTheme));
        ApplyTheme();
    }

    private void ToggleTheme()
    {
        _settings.Theme = IsLightTheme ? ThemeManager.Dark : ThemeManager.Light;
        Persist();
        ApplyTheme();
        OnPropertyChanged(nameof(AppTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        StatusText = $"Theme: {_settings.Theme}";
    }

    private void ApplyTheme()
    {
        ThemeManager.Apply(_settings.Theme);
        OnPropertyChanged(nameof(AppTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }
}
