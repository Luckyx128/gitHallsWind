using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace GitHalls.App.Themes;

/// <summary>
/// Syntax token categories the diff renderer knows how to colour. Deliberately
/// coarse: the highlighting library's own scope names are mapped onto these, so
/// swapping the library later doesn't touch the theme.
/// </summary>
public enum DiffTokenKind
{
    Plain,
    Keyword,
    Comment,
    String,
    Number,
    Type,
    Preprocessor,
    Attribute,
    Operator
}

/// <summary>
/// Colours and metrics for the diff renderer, in light and dark.
/// Port of DiffTextTheme.swift.
/// </summary>
public sealed class DiffTextTheme
{
    public required Color ViewBackground { get; init; }
    public required Color BaseText { get; init; }
    public required Color HunkHeaderText { get; init; }
    public required Color HunkHeaderBackground { get; init; }
    public required Color AdditionBackground { get; init; }
    public required Color DeletionBackground { get; init; }
    public required Color GutterBackground { get; init; }
    public required Color GutterText { get; init; }
    public required Color GutterSeparator { get; init; }
    public required Color AdditionMarker { get; init; }
    public required Color DeletionMarker { get; init; }
    public required Color SearchHighlight { get; init; }

    public required IReadOnlyDictionary<DiffTokenKind, Color> Tokens { get; init; }

    public const string FontFamily = "Cascadia Mono, Consolas, Courier New";
    public const double FontSize = 13;
    public const double GutterFontSize = 12;
    public const double LineHeight = 19;

    public Color TokenColor(DiffTokenKind kind) => Tokens.TryGetValue(kind, out var color) ? color : BaseText;

    /// <summary>Row tint behind a line, or null when the line needs none.</summary>
    public Color? LineBackground(Core.Models.DiffLineType type) => type switch
    {
        Core.Models.DiffLineType.Addition => AdditionBackground,
        Core.Models.DiffLineType.Deletion => DeletionBackground,
        Core.Models.DiffLineType.HunkHeader => HunkHeaderBackground,
        _ => null
    };

    public static DiffTextTheme For(ElementTheme theme) => theme == ElementTheme.Dark ? Dark : Light;

    public static readonly DiffTextTheme Light = new()
    {
        ViewBackground = Color.FromArgb(255, 255, 255, 255),
        BaseText = Color.FromArgb(255, 33, 33, 33),
        HunkHeaderText = Color.FromArgb(255, 102, 102, 102),
        HunkHeaderBackground = Color.FromArgb(23, 51, 115, 230),
        AdditionBackground = Color.FromArgb(38, 56, 184, 77),
        DeletionBackground = Color.FromArgb(36, 230, 61, 61),
        GutterBackground = Color.FromArgb(255, 246, 246, 246),
        GutterText = Color.FromArgb(255, 148, 148, 148),
        GutterSeparator = Color.FromArgb(255, 219, 219, 219),
        AdditionMarker = Color.FromArgb(255, 41, 153, 61),
        DeletionMarker = Color.FromArgb(255, 204, 46, 46),
        SearchHighlight = Color.FromArgb(120, 255, 214, 0),
        Tokens = new Dictionary<DiffTokenKind, Color>
        {
            [DiffTokenKind.Plain] = Color.FromArgb(255, 33, 33, 33),
            [DiffTokenKind.Keyword] = Color.FromArgb(255, 155, 35, 147),
            [DiffTokenKind.Comment] = Color.FromArgb(255, 93, 108, 121),
            [DiffTokenKind.String] = Color.FromArgb(255, 196, 26, 22),
            [DiffTokenKind.Number] = Color.FromArgb(255, 28, 0, 207),
            [DiffTokenKind.Type] = Color.FromArgb(255, 63, 110, 116),
            [DiffTokenKind.Preprocessor] = Color.FromArgb(255, 100, 56, 32),
            [DiffTokenKind.Attribute] = Color.FromArgb(255, 130, 96, 30),
            [DiffTokenKind.Operator] = Color.FromArgb(255, 33, 33, 33),
        }
    };

    public static readonly DiffTextTheme Dark = new()
    {
        ViewBackground = Color.FromArgb(255, 31, 32, 38),
        BaseText = Color.FromArgb(255, 224, 224, 224),
        HunkHeaderText = Color.FromArgb(255, 158, 158, 158),
        HunkHeaderBackground = Color.FromArgb(41, 89, 140, 255),
        AdditionBackground = Color.FromArgb(41, 77, 217, 102),
        DeletionBackground = Color.FromArgb(41, 255, 89, 89),
        GutterBackground = Color.FromArgb(255, 37, 38, 45),
        GutterText = Color.FromArgb(255, 122, 122, 122),
        GutterSeparator = Color.FromArgb(255, 77, 77, 77),
        AdditionMarker = Color.FromArgb(255, 102, 217, 115),
        DeletionMarker = Color.FromArgb(255, 255, 115, 115),
        SearchHighlight = Color.FromArgb(120, 255, 193, 7),
        Tokens = new Dictionary<DiffTokenKind, Color>
        {
            [DiffTokenKind.Plain] = Color.FromArgb(255, 224, 224, 224),
            [DiffTokenKind.Keyword] = Color.FromArgb(255, 252, 95, 163),
            [DiffTokenKind.Comment] = Color.FromArgb(255, 124, 139, 152),
            [DiffTokenKind.String] = Color.FromArgb(255, 252, 106, 93),
            [DiffTokenKind.Number] = Color.FromArgb(255, 208, 191, 105),
            [DiffTokenKind.Type] = Color.FromArgb(255, 93, 216, 255),
            [DiffTokenKind.Preprocessor] = Color.FromArgb(255, 253, 143, 63),
            [DiffTokenKind.Attribute] = Color.FromArgb(255, 191, 160, 100),
            [DiffTokenKind.Operator] = Color.FromArgb(255, 224, 224, 224),
        }
    };
}
