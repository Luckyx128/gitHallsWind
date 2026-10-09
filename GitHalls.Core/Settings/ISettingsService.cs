using System;

namespace GitHalls.Core.Settings;

public class DiffSettings
{
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 14.0;
    public string EditorTheme { get; set; } = "Light";
}

public interface ISettingsService
{
    DiffSettings GetDiffSettings();
    void SaveDiffSettings(DiffSettings settings);
}
