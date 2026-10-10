using Microsoft.UI.Xaml.Data;
using System;
using GitHalls.Core.Jira;
using GitHalls.App.ViewModels;

namespace GitHalls.App.Converters;

public class TimerStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // This is tricky because we need both the Issue and the ClockifyViewModel.
        return Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
