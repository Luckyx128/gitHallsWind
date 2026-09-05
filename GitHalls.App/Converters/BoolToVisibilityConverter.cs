using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GitHalls.App.Converters;

/// <summary>
/// Bool to Visibility. Pass "Invert" as the parameter to flip it.
/// WinUI 3 dropped the built-in UWP converter, so bindings need this.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility visibility && visibility == Visibility.Visible;
}
