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

    /// <summary>Tint over a row picked for staging. Sits on top of the +/- tint.</summary>
    public required Color SelectionBackground { get; init; }

    /// <summary>Tint under the cursor in the selection column.</summary>
    public required Color HoverBackground { get; init; }

    /// <summary>The check drawn in the selection column of a picked row.</summary>
    public required Color SelectionMark { get; init; }

    /// <summary>The same mark before it is picked — visible only on hover.</summary>
    public required Color SelectionMarkIdle { get; init; }

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

    // The addition and deletion values below are GHAdditionColor and
    // GHDeletionColor from Styles/AppStyles.xaml, which is where the palette is
    // decided; change them there first. They are repeated here rather than read
    // from the ResourceDictionary because DiffTextView builds a SolidColorBrush
    // per rendered line, and a resource lookup on that path is exactly the cost
    // MaxRenderedLines exists to avoid.

    public static readonly DiffTextTheme Light = new()
    {
        ViewBackground = Color.FromArgb(255, 255, 255, 255),
        BaseText = Color.FromArgb(255, 33, 33, 33),
        HunkHeaderText = Color.FromArgb(255, 102, 102, 102),
        HunkHeaderBackground = Color.FromArgb(23, 51, 115, 230),
        AdditionBackground = Color.FromArgb(38, 10, 107, 10),
        DeletionBackground = Color.FromArgb(36, 188, 40, 25),
        GutterBackground = Color.FromArgb(255, 246, 246, 246),
        GutterText = Color.FromArgb(255, 148, 148, 148),
        GutterSeparator = Color.FromArgb(255, 219, 219, 219),
        AdditionMarker = Color.FromArgb(255, 10, 107, 10),    // GHAdditionColor #0A6B0A
        DeletionMarker = Color.FromArgb(255, 188, 40, 25),    // GHDeletionColor #BC2819
        SearchHighlight = Color.FromArgb(120, 255, 214, 0),
        SelectionBackground = Color.FromArgb(46, 51, 115, 230),
        HoverBackground = Color.FromArgb(20, 51, 115, 230),
        SelectionMark = Color.FromArgb(255, 51, 115, 230),
        SelectionMarkIdle = Color.FromArgb(255, 176, 176, 176),
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
        AdditionBackground = Color.FromArgb(41, 108, 203, 95),
        DeletionBackground = Color.FromArgb(41, 255, 153, 164),
        GutterBackground = Color.FromArgb(255, 37, 38, 45),
        GutterText = Color.FromArgb(255, 122, 122, 122),
        GutterSeparator = Color.FromArgb(255, 77, 77, 77),
        AdditionMarker = Color.FromArgb(255, 108, 203, 95),   // GHAdditionColor #6CCB5F
        DeletionMarker = Color.FromArgb(255, 255, 153, 164),  // GHDeletionColor #FF99A4
        SearchHighlight = Color.FromArgb(120, 255, 193, 7),
        SelectionBackground = Color.FromArgb(56, 89, 140, 255),
        HoverBackground = Color.FromArgb(26, 89, 140, 255),
        SelectionMark = Color.FromArgb(255, 118, 163, 255),
        SelectionMarkIdle = Color.FromArgb(255, 110, 110, 110),
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
