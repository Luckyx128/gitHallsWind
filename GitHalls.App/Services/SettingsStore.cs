using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHalls.App.Services;

public class AppSettings
{
    public List<string> RecentRepositories { get; set; } = new();
    public string? LastOpenedRepository { get; set; }
}

/// <summary>
/// Source-generated serializer for <see cref="AppSettings"/>. Required, not an
/// optimization: the app publishes with PublishTrimmed, which strips the
/// reflection metadata the default JsonSerializer path relies on — settings
/// would silently come back empty in a published build only.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext
{
}

public class SettingsStore
{
    private readonly string _settingsFilePath;

    public SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "GitHalls");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsFilePath)) return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath);
            return JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            // A corrupt or half-written settings file must never stop the app
            // from opening — start over from defaults.
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        catch
        {
            // Losing the recents list is not worth crashing over.
        }
    }
}
