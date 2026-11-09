using System.Windows;

namespace FileDiff.App.Infrastructure;

/// <summary>Swaps the palette dictionary (Light/Dark) while keeping all other theme resources.</summary>
public static class ThemeManager
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static string Current { get; private set; } = Light;

    public static void Apply(string? theme)
    {
        var name = string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;
        Current = name;

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        ResourceDictionary? palette = null;
        foreach (var dict in merged)
        {
            if (dict.Source is not null && dict.Source.OriginalString.Contains("Palette", StringComparison.OrdinalIgnoreCase))
            {
                palette = dict;
                break;
            }
        }

        if (palette is null)
        {
            return;
        }

        var uri = new Uri($"Themes/Palette.{name}.xaml", UriKind.Relative);
        try
        {
            var next = new ResourceDictionary { Source = uri };
            var index = merged.IndexOf(palette);
            merged[index] = next;
        }
        catch
        {
            // Keep current palette if the alternate file is missing (e.g. in tests).
        }
    }
}
