using System;
using System.IO;
using System.Text.Json;

namespace GitHalls.Core.Settings;

public class DefaultSettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private DiffSettings _diffSettings;

    public DefaultSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "GitHalls");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "diff_settings.json");

        if (File.Exists(_settingsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                _diffSettings = JsonSerializer.Deserialize<DiffSettings>(json) ?? new DiffSettings();
            }
            catch
            {
                _diffSettings = new DiffSettings();
            }
        }
        else
        {
            _diffSettings = new DiffSettings();
        }
    }

    public DiffSettings GetDiffSettings()
    {
        return _diffSettings;
    }

    public void SaveDiffSettings(DiffSettings settings)
    {
        _diffSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        try
        {
            var json = JsonSerializer.Serialize(_diffSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignore write errors to prevent crashing
        }
    }
}
