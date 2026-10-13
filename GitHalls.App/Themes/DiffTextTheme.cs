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

    public static string FontFamily { get; set; } = "Cascadia Mono, Consolas, Courier New";
    public static double FontSize { get; set; } = 13;
    public static double GutterFontSize { get; set; } = 12;
    public static double LineHeight { get; set; } = 19;

    public static event EventHandler? MetricsChanged;

    public static void UpdateMetrics(string fontFamily, double fontSize, double gutterFontSize, double lineHeight)
    {
        FontFamily = fontFamily;
        FontSize = fontSize;
        GutterFontSize = gutterFontSize;
        LineHeight = lineHeight;
        MetricsChanged?.Invoke(null, EventArgs.Empty);
    }

    public Color TokenColor(DiffTokenKind kind) => Tokens.TryGetValue(kind, out var color) ? color : BaseText;

    /// <summary>Row tint behind a line, or null when the line needs none.</summary>
    public Color? LineBackground(Core.Models.DiffLineType type) => type switch
    {
        Core.Models.DiffLineType.Addition => AdditionBackground,
        Core.Models.DiffLineType.Deletion => DeletionBackground,
        Core.Models.DiffLineType.HunkHeader => HunkHeaderBackground,
        _ => null
    };

    public const string ThemeSystem = "Padrão do Sistema";
    public const string ThemeCatppuccinMocha = "Catppuccin Mocha";
    public const string ThemeCatppuccinLatte = "Catppuccin Latte";
    public const string ThemeDracula = "Dracula";

    public static readonly IReadOnlyList<string> AvailableThemes =
    [
        ThemeSystem,
        ThemeCatppuccinMocha,
        ThemeCatppuccinLatte,
        ThemeDracula
    ];

    public static string ActiveThemeName { get; private set; } = ThemeSystem;
    public static event EventHandler? ThemeChanged;

    public static void SetActiveTheme(string? themeName)
    {
        ActiveThemeName = string.IsNullOrWhiteSpace(themeName) ? ThemeSystem : themeName;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static DiffTextTheme For(ElementTheme theme) => ActiveThemeName switch
    {
        ThemeCatppuccinMocha => CatppuccinMocha,
        ThemeCatppuccinLatte => CatppuccinLatte,
        ThemeDracula => Dracula,
        _ => theme == ElementTheme.Dark ? Dark : Light
    };

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

    public static readonly DiffTextTheme CatppuccinMocha = new()
    {
        ViewBackground = Color.FromArgb(255, 30, 30, 46),       // Base #1E1E2E
        BaseText = Color.FromArgb(255, 205, 214, 244),         // Text #CDD6F4
        HunkHeaderText = Color.FromArgb(255, 137, 180, 250),   // Blue #89B4FA
        HunkHeaderBackground = Color.FromArgb(38, 137, 180, 250),
        AdditionBackground = Color.FromArgb(38, 166, 227, 161), // Green #A6E3A1
        DeletionBackground = Color.FromArgb(38, 243, 139, 168), // Red #F38BA8
        GutterBackground = Color.FromArgb(255, 24, 24, 37),     // Mantle #181825
        GutterText = Color.FromArgb(255, 108, 112, 134),       // Overlay0 #6C7086
        GutterSeparator = Color.FromArgb(255, 49, 50, 68),      // Surface0 #313244
        AdditionMarker = Color.FromArgb(255, 166, 227, 161),   // Green #A6E3A1
        DeletionMarker = Color.FromArgb(255, 243, 139, 168),   // Red #F38BA8
        SearchHighlight = Color.FromArgb(120, 249, 226, 175),  // Yellow #F9E2AF
        SelectionBackground = Color.FromArgb(50, 180, 190, 254), // Lavender #B4BEFE
        HoverBackground = Color.FromArgb(25, 180, 190, 254),
        SelectionMark = Color.FromArgb(255, 180, 190, 254),    // Lavender #B4BEFE
        SelectionMarkIdle = Color.FromArgb(255, 88, 91, 112),  // Surface2 #585B70
        Tokens = new Dictionary<DiffTokenKind, Color>
        {
            [DiffTokenKind.Plain] = Color.FromArgb(255, 205, 214, 244),        // Text #CDD6F4
            [DiffTokenKind.Keyword] = Color.FromArgb(255, 203, 166, 247),      // Mauve #CBA6F7
            [DiffTokenKind.Comment] = Color.FromArgb(255, 108, 112, 134),      // Overlay0 #6C7086
            [DiffTokenKind.String] = Color.FromArgb(255, 166, 227, 161),       // Green #A6E3A1
            [DiffTokenKind.Number] = Color.FromArgb(255, 250, 179, 135),       // Peach #FAB387
            [DiffTokenKind.Type] = Color.FromArgb(255, 249, 226, 175),         // Yellow #F9E2AF
            [DiffTokenKind.Preprocessor] = Color.FromArgb(255, 245, 194, 231), // Pink #F5C2E7
            [DiffTokenKind.Attribute] = Color.FromArgb(255, 137, 180, 250),    // Blue #89B4FA
            [DiffTokenKind.Operator] = Color.FromArgb(255, 137, 220, 235),     // Sky #89DCEB
        }
    };

    public static readonly DiffTextTheme CatppuccinLatte = new()
    {
        ViewBackground = Color.FromArgb(255, 239, 241, 245),   // Base #EFF1F5
        BaseText = Color.FromArgb(255, 76, 79, 105),           // Text #4C4F69
        HunkHeaderText = Color.FromArgb(255, 30, 102, 245),     // Blue #1E66F5
        HunkHeaderBackground = Color.FromArgb(30, 30, 102, 245),
        AdditionBackground = Color.FromArgb(38, 64, 160, 43),   // Green #40A02B
        DeletionBackground = Color.FromArgb(38, 210, 15, 57),   // Red #D20F39
        GutterBackground = Color.FromArgb(255, 230, 233, 239), // Mantle #E6E9EF
        GutterText = Color.FromArgb(255, 156, 160, 176),       // Overlay0 #9CA0B0
        GutterSeparator = Color.FromArgb(255, 204, 208, 218),  // Surface0 #CCD0DA
        AdditionMarker = Color.FromArgb(255, 64, 160, 43),     // Green #40A02B
        DeletionMarker = Color.FromArgb(255, 210, 15, 57),     // Red #D20F39
        SearchHighlight = Color.FromArgb(120, 223, 142, 29),   // Yellow #DF8E1D
        SelectionBackground = Color.FromArgb(45, 114, 135, 253), // Lavender #7287FD
        HoverBackground = Color.FromArgb(22, 114, 135, 253),
        SelectionMark = Color.FromArgb(255, 114, 135, 253),    // Lavender #7287FD
        SelectionMarkIdle = Color.FromArgb(255, 188, 192, 204), // Surface1 #BCC0CC
        Tokens = new Dictionary<DiffTokenKind, Color>
        {
            [DiffTokenKind.Plain] = Color.FromArgb(255, 76, 79, 105),          // Text #4C4F69
            [DiffTokenKind.Keyword] = Color.FromArgb(255, 136, 57, 239),       // Mauve #8839EF
            [DiffTokenKind.Comment] = Color.FromArgb(255, 156, 160, 176),      // Overlay0 #9CA0B0
            [DiffTokenKind.String] = Color.FromArgb(255, 64, 160, 43),         // Green #40A02B
            [DiffTokenKind.Number] = Color.FromArgb(255, 254, 100, 11),        // Peach #FE640B
            [DiffTokenKind.Type] = Color.FromArgb(255, 223, 142, 29),          // Yellow #DF8E1D
            [DiffTokenKind.Preprocessor] = Color.FromArgb(255, 234, 118, 203), // Pink #EA76CB
            [DiffTokenKind.Attribute] = Color.FromArgb(255, 30, 102, 245),     // Blue #1E66F5
            [DiffTokenKind.Operator] = Color.FromArgb(255, 4, 165, 229),       // Sky #04A5E5
        }
    };

    public static readonly DiffTextTheme Dracula = new()
    {
        ViewBackground = Color.FromArgb(255, 40, 42, 54),       // Background #282A36
        BaseText = Color.FromArgb(255, 248, 248, 242),         // Foreground #F8F8F2
        HunkHeaderText = Color.FromArgb(255, 189, 147, 249),   // Purple #BD93F9
        HunkHeaderBackground = Color.FromArgb(38, 189, 147, 249),
        AdditionBackground = Color.FromArgb(40, 80, 250, 123),  // Green #50FA7B
        DeletionBackground = Color.FromArgb(40, 255, 85, 85),   // Red #FF5555
        GutterBackground = Color.FromArgb(255, 33, 34, 44),     // Darker Background #21222C
        GutterText = Color.FromArgb(255, 98, 114, 164),        // Comment #6272A4
        GutterSeparator = Color.FromArgb(255, 68, 71, 90),      // Selection #44475A
        AdditionMarker = Color.FromArgb(255, 80, 250, 123),    // Green #50FA7B
        DeletionMarker = Color.FromArgb(255, 255, 85, 85),     // Red #FF5555
        SearchHighlight = Color.FromArgb(120, 241, 250, 140),  // Yellow #F1FA8C
        SelectionBackground = Color.FromArgb(50, 189, 147, 249), // Purple #BD93F9
        HoverBackground = Color.FromArgb(25, 189, 147, 249),
        SelectionMark = Color.FromArgb(255, 189, 147, 249),    // Purple #BD93F9
        SelectionMarkIdle = Color.FromArgb(255, 98, 114, 164),  // Comment #6272A4
        Tokens = new Dictionary<DiffTokenKind, Color>
        {
            [DiffTokenKind.Plain] = Color.FromArgb(255, 248, 248, 242),        // Foreground #F8F8F2
            [DiffTokenKind.Keyword] = Color.FromArgb(255, 255, 121, 198),      // Pink #FF79C6
            [DiffTokenKind.Comment] = Color.FromArgb(255, 98, 114, 164),       // Comment #6272A4
            [DiffTokenKind.String] = Color.FromArgb(255, 241, 250, 140),       // Yellow #F1FA8C
            [DiffTokenKind.Number] = Color.FromArgb(255, 189, 147, 249),       // Purple #BD93F9
            [DiffTokenKind.Type] = Color.FromArgb(255, 139, 233, 253),         // Cyan #8BE9FD
            [DiffTokenKind.Preprocessor] = Color.FromArgb(255, 255, 184, 108), // Orange #FFB86C
            [DiffTokenKind.Attribute] = Color.FromArgb(255, 80, 250, 123),     // Green #50FA7B
            [DiffTokenKind.Operator] = Color.FromArgb(255, 255, 121, 198),     // Pink #FF79C6
        }
    };
}
