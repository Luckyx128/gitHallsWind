using System.Text.Json;
using System.Text.Json.Serialization;
using GitHalls.Core.Jira;
using GitHalls.Core.Models;

namespace GitHalls.App.Services;

public class AppSettings
{
    public List<string> RecentRepositories { get; set; } = new();
    public string? LastOpenedRepository { get; set; }

    /// <summary>Recently checked-out branches, keyed by repository path.</summary>
    public Dictionary<string, List<string>> RecentBranches { get; set; } = new();

    /// <summary>Jira site and account. The API token is not here — it lives in
    /// the Windows Credential Manager, see <see cref="JiraAccountStore"/>.</summary>
    public string? JiraSite { get; set; }
    public string JiraEmail { get; set; } = string.Empty;

    /// <summary>Queries the user wrote. The presets are code (<see cref="JiraQueryPresets"/>), not settings.</summary>
    public List<JiraQuery> JiraCustomQueries { get; set; } = new();

    /// <summary>Which query the board showed last, preset or custom.</summary>
    public string? JiraSelectedQueryId { get; set; }

    /// <summary>Saved git identities the user switches between.</summary>
    public List<GitIdentity> GitIdentities { get; set; } = new();

    /// <summary>Show diffs side by side rather than unified.</summary>
    public bool SideBySideDiff { get; set; }
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

    /// <summary>
    /// The settings as they currently stand. Held here because the file has more
    /// than one writer — recents and identities come from the repository view
    /// model, the Jira account from its own screen — and each writing its own
    /// freshly built object would drop whatever the other had just saved.
    /// </summary>
    public AppSettings Current { get; private set; } = new();

    public SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "GitHalls");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        Current = await ReadAsync();
        return Current;
    }

    private async Task<AppSettings> ReadAsync()
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

    /// <summary>
    /// Changes part of the settings and writes the whole file back. Every writer
    /// goes through here, so no screen has to know what the others own.
    /// </summary>
    public Task UpdateAsync(Action<AppSettings> change)
    {
        change(Current);
        return SaveAsync(Current);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;

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
