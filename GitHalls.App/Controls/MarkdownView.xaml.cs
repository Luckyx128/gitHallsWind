using GitHalls.Core.Markdown;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace GitHalls.App.Controls;

/// <summary>
/// Lays out parsed Markdown blocks.
///
/// The parsing is all in <see cref="MarkdownParser"/>, which is why this is
/// only sizes and spacing: the decisions worth testing are not in here.
/// </summary>
public sealed partial class MarkdownView : UserControl
{
    public MarkdownView()
    {
        InitializeComponent();
    }

    /// <summary>Replaces what is shown. Pass null or empty to clear.</summary>
    public void SetBlocks(IReadOnlyList<MarkdownBlock>? blocks)
    {
        BlocksPanel.Children.Clear();
        if (blocks == null) return;

        foreach (var block in blocks)
        {
            var element = Build(block);
            if (element != null) BlocksPanel.Children.Add(element);
        }
    }

    private UIElement? Build(MarkdownBlock block) => block switch
    {
        MarkdownBlock.Heading heading => Heading(heading),
        MarkdownBlock.Paragraph paragraph => Rich(paragraph.Spans),
        MarkdownBlock.ListItem item => ListItem(item),
        MarkdownBlock.Quote quote => Quote(quote),
        MarkdownBlock.Code code => Monospaced(code.Text),
        MarkdownBlock.Table table => Monospaced(string.Join("\n", table.Rows)),
        MarkdownBlock.Rule _ => new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 4)
        },
        _ => null
    };

    private static UIElement Heading(MarkdownBlock.Heading heading)
    {
        var text = Rich(heading.Spans);
        text.FontSize = heading.Level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 };
        text.FontWeight = FontWeights.SemiBold;
        text.Margin = new Thickness(0, heading.Level <= 2 ? 8 : 4, 0, 0);

        return text;
    }

    private static UIElement ListItem(MarkdownBlock.ListItem item)
    {
        var marker = new TextBlock
        {
            Text = item.Marker,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            MinWidth = 20
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };
        row.Children.Add(marker);
        row.Children.Add(Rich(item.Spans));

        return row;
    }

    private static UIElement Quote(MarkdownBlock.Quote quote)
    {
        // The bar is the quote; a box would compete with the code blocks.
        var bar = new Border
        {
            Width = 3,
            Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
        };

        var body = Rich(quote.Spans);
        body.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(bar);
        row.Children.Add(body);

        return row;
    }

    private static UIElement Monospaced(string text) => new Border
    {
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true
        }
    };

    /// <summary>
    /// A RichTextBlock rather than a TextBlock: a link inside a line has to be
    /// its own inline, and only this one takes a Hyperlink.
    /// </summary>
    private static RichTextBlock Rich(IReadOnlyList<MarkdownSpan> spans)
    {
        var paragraph = new Paragraph();

        foreach (var span in spans)
        {
            paragraph.Inlines.Add(BuildInline(span));
        }

        var text = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        text.Blocks.Add(paragraph);
        return text;
    }

    private static Inline BuildInline(MarkdownSpan span)
    {
        var run = new Run { Text = span.Text };

        switch (span.Style)
        {
            case MarkdownSpanStyle.Bold:
                run.FontWeight = FontWeights.SemiBold;
                break;

            case MarkdownSpanStyle.Italic:
                run.FontStyle = FontStyle.Italic;
                break;

            case MarkdownSpanStyle.Code:
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                break;

            case MarkdownSpanStyle.Link when Uri.TryCreate(span.Url, UriKind.Absolute, out var uri):
                var link = new Hyperlink { NavigateUri = uri };
                link.Inlines.Add(run);
                return link;
        }

        return run;
    }
}
