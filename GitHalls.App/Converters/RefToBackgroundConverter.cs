using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace GitHalls.App.Converters;

public class RefToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string refName)
        {
            if (refName.StartsWith("origin/"))
            {
                // Remote branch - Light blue/teal tint
                return Application.Current.Resources["GHModifiedTintBrush"] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            if (refName.StartsWith("tag: "))
            {
                // Tag - Yellow/Orange tint
                return Application.Current.Resources["GHConflictTintBrush"] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            
            // Local branch - Green tint
            return Application.Current.Resources["GHAdditionTintBrush"] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        
        return Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
