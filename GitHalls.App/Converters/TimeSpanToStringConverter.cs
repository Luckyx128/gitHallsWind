using Microsoft.UI.Xaml.Data;
using System;

namespace GitHalls.App.Converters;

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is TimeSpan ts)
        {
            return ts.ToString(@"hh\:mm\:ss");
        }
        return "00:00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
