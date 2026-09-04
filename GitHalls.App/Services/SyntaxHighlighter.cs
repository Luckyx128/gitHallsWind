using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace GitHalls.App.Services;

public class SyntaxHighlighter
{
    private static readonly SolidColorBrush DefaultBrush = new(Colors.LightGray);
    private static readonly SolidColorBrush KeywordBrush = new(Colors.CornflowerBlue);
    private static readonly SolidColorBrush StringBrush = new(Colors.LightCoral);
    private static readonly SolidColorBrush CommentBrush = new(Colors.LightGreen);
    private static readonly SolidColorBrush AdditionBrush = new(Colors.MediumSeaGreen);
    private static readonly SolidColorBrush DeletionBrush = new(Colors.IndianRed);

    public IEnumerable<(string text, SolidColorBrush brush)> HighlightDiffLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield return (line, DefaultBrush);
            yield break;
        }

        if (line.StartsWith("+"))
        {
            yield return (line, AdditionBrush);
            yield break;
        }
        
        if (line.StartsWith("-"))
        {
            yield return (line, DeletionBrush);
            yield break;
        }

        if (line.StartsWith("@@"))
        {
            yield return (line, KeywordBrush);
            yield break;
        }

        // Very basic tokenizer for context lines
        // A real syntax highlighter would parse C#/Swift/etc keywords.
        // For now, we'll just return it as default color to get things rendering.
        yield return (line, DefaultBrush);
    }
}
