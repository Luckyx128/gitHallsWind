using System;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace GitHalls.App.Converters;

/// <summary>
/// Converts a FileChangeStatus enum value to a human-readable description
/// for accessibility (AutomationProperties.Name) and tooltips.
/// </summary>
public class FileStatusToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is FileChangeStatus status)
        {
            return status switch
            {
                FileChangeStatus.Modified => "Modified",
                FileChangeStatus.Added => "Added",
                FileChangeStatus.Deleted => "Deleted",
                FileChangeStatus.Renamed => "Renamed",
                FileChangeStatus.Copied => "Copied",
                FileChangeStatus.Untracked => "Untracked",
                FileChangeStatus.Unmerged => "Conflicted",
                _ => status.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
