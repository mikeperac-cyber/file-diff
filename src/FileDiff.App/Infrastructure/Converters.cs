using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FileDiff.Core.Diffing;

namespace FileDiff.App.Infrastructure;

/// <summary>True shows, false collapses. Pass "Invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A count of zero hides the element; anything else shows it. Pass "Invert" to flip.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value is int number ? number : 0;
        var hasAny = count > 0;

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasAny = !hasAny;
        }

        return hasAny ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Null or empty collapses the element.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is not null && !(value is string text && text.Length == 0);

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            present = !present;
        }

        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Maps a <see cref="ChangeKind"/> onto the shared badge brushes in the theme.</summary>
public sealed class ChangeKindBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value is ChangeKind change ? change : ChangeKind.Modified;
        var part = parameter as string ?? "Background";
        var key = $"Kind.{kind}.{part}";

        return Application.Current?.TryFindResource(key) as Brush
               ?? Application.Current?.TryFindResource($"Kind.Modified.{part}") as Brush
               ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Highlights added / removed diff lines using the same palette as the badges.</summary>
public sealed class DiffLineBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value is DiffLineKind line ? line : DiffLineKind.Context;
        var role = kind switch
        {
            DiffLineKind.Added => "Added",
            DiffLineKind.Removed => "Removed",
            _ => "Context",
        };

        return Application.Current?.TryFindResource($"Diff.{role}.{parameter ?? "Foreground"}") as Brush
               ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
