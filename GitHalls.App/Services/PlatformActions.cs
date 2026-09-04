using System.Diagnostics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitHalls.App.Services;

public class PlatformActions
{
    public async Task<string?> PickFolderAsync(Microsoft.UI.Xaml.Window window)
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public void RevealInExplorer(string path)
    {
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    public void OpenTerminal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback to powershell if Windows Terminal is not installed
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = path,
                UseShellExecute = true
            });
        }
    }
}
