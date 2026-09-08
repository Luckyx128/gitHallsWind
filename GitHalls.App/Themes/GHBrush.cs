using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GitHalls.App.Themes;

/// <summary>
/// Reads a palette brush out of the theme dictionary that is active now.
///
/// The point is the "now". A brush pulled from Application.Current.Resources
/// into a field keeps the theme it was born in: switching light to dark with
/// the window open swaps the dictionary, and whoever cached the old instance
/// goes on painting the old colour. Everything here is resolved at the moment
/// it is used, and the views that build visuals in code re-apply on
/// ActualThemeChanged.
///
/// XAML has no need of this — {ThemeResource} already does it.
/// </summary>
internal static class GHBrush
{
    public static Brush Get(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    /// <summary>
    /// Jira's three status categories, in the app's own state colours. Shared by
    /// the board's column headers and the issue window's status dot, which had
    /// each spelled out the same three-way switch.
    /// </summary>
    public static Brush JiraCategory(string? category) => Get(category switch
    {
        "done" => "GHAdditionBrush",
        "indeterminate" => "GHModifiedBrush",
        _ => "GHNeutralBrush"
    });
}
