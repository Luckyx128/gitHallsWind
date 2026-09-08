using System;
using GitHalls.App.Themes;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace GitHalls.App.Converters;

/// <summary>
/// The brush a file status is badged in. Replaces the hex string Core used to
/// hand out: this resolves a theme resource, so the badge follows light and
/// dark instead of being one fixed colour.
///
/// Set <see cref="Tint"/> for the badge fill and leave it false for the letter.
/// The lookup happens on every Convert, not once into a field — see GHBrush.
/// </summary>
public class FileStatusToBrushConverter : IValueConverter
{
    /// <summary>True for the low-opacity fill behind the letter.</summary>
    public bool Tint { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is FileChangeStatus status ? status switch
        {
            FileChangeStatus.Added or FileChangeStatus.Untracked => "GHAddition",
            FileChangeStatus.Modified => "GHModified",
            FileChangeStatus.Deleted => "GHDeletion",
            FileChangeStatus.Renamed or FileChangeStatus.Copied => "GHRenamed",
            FileChangeStatus.Unmerged => "GHConflict",
            _ => "GHNeutral"
        } : "GHNeutral";

        return GHBrush.Get(key + (Tint ? "TintBrush" : "Brush"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
