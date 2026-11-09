using System.Windows;
using System.Windows.Controls;

namespace FileDiff.App.Controls;

/// <summary>Minimal text-input dialog for commander ops (F6 rename, F7 mkdir). No XAML needed.</summary>
public sealed class PromptDialog : Window
{
    private readonly TextBox _box;

    private PromptDialog(string title, string label, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = initial };
        panel.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        _box.Focus();
        _box.SelectAll();
    }

    public string Value => _box.Text;

    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var dlg = new PromptDialog(title, label, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Value.Trim() : null;
    }
}
