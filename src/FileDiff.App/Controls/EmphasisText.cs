using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FileDiff.App.Controls;

/// <summary>
/// Renders one line of a diff. When the line has an unchanged frame around the part that
/// actually changed, that part is highlighted; otherwise the line is shown plainly.
/// <para>
/// This is a code-built control rather than pure XAML because a single wrapping line has to be
/// assembled as inline runs — separate TextBlocks inside a horizontal StackPanel never wrap.
/// </para>
/// </summary>
public class EmphasisText : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmphasisText),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty PrefixProperty =
        DependencyProperty.Register(nameof(Prefix), typeof(string), typeof(EmphasisText),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty EmphasisProperty =
        DependencyProperty.Register(nameof(Emphasis), typeof(string), typeof(EmphasisText),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(EmphasisText),
            new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty HasEmphasisProperty =
        DependencyProperty.Register(nameof(HasEmphasis), typeof(bool), typeof(EmphasisText),
            new PropertyMetadata(false, OnChanged));

    public EmphasisText()
    {
        SnapsToDevicePixels = true;
        Rebuild();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Prefix
    {
        get => (string)GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    public string Emphasis
    {
        get => (string)GetValue(EmphasisProperty);
        set => SetValue(EmphasisProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public bool HasEmphasis
    {
        get => (bool)GetValue(HasEmphasisProperty);
        set => SetValue(HasEmphasisProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EmphasisText)d).Rebuild();

    private void Rebuild()
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
        };

        if (HasEmphasis)
        {
            block.Inlines.Add(new Run(Prefix ?? string.Empty));

            var emphasis = new Run(Emphasis ?? string.Empty) { FontWeight = FontWeights.SemiBold };
            emphasis.SetResourceReference(Run.BackgroundProperty, "Diff.Emphasis");
            block.Inlines.Add(emphasis);

            block.Inlines.Add(new Run(Suffix ?? string.Empty));
        }
        else
        {
            block.Inlines.Add(new Run(Text ?? string.Empty));
        }

        Content = block;
    }
}
