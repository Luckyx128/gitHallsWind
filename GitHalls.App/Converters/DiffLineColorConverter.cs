using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using GitHalls.Core.Models;
using Windows.UI;

namespace GitHalls.App.Converters;

public class DiffLineColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DiffLineType type)
        {
            bool isBackground = parameter?.ToString() == "Background";

            if (isBackground)
            {
                return type switch
                {
                    DiffLineType.Addition => new SolidColorBrush(Color.FromArgb(40, 40, 167, 69)), // Light Green with alpha
                    DiffLineType.Deletion => new SolidColorBrush(Color.FromArgb(40, 215, 58, 73)), // Light Red with alpha
                    DiffLineType.Header => new SolidColorBrush(Color.FromArgb(40, 3, 102, 214)), // Light Blue with alpha
                    _ => new SolidColorBrush(Colors.Transparent)
                };
            }
            else // Foreground
            {
                return type switch
                {
                    DiffLineType.Addition => new SolidColorBrush(Color.FromArgb(255, 60, 200, 90)),
                    DiffLineType.Deletion => new SolidColorBrush(Color.FromArgb(255, 230, 70, 85)),
                    DiffLineType.Header => new SolidColorBrush(Color.FromArgb(255, 100, 150, 255)),
                    _ => App.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? new SolidColorBrush(Colors.White)
                };
            }
        }
        
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
