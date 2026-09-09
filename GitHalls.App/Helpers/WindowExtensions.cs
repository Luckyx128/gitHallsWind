using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace GitHalls.App.Helpers;

public static class WindowExtensions
{
    public static void ConfigureCustomTitleBar(this Window window, UIElement titleBarElement)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBarElement);

        var titleBar = window.AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
}
