using System.Text.Json.Serialization;

namespace GitHalls.Core.Jira;

/// <summary>
/// One named JQL query the board can run: either a preset the app ships with
/// or one the user wrote and saved.
/// </summary>
public sealed class JiraQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Jql { get; set; } = string.Empty;

    /// <summary>Presets are code, not settings: they are never written to disk.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    public JiraQuery()
    {
    }

    public JiraQuery(string id, string name, string jql, bool isBuiltIn = false)
    {
        Id = id;
        Name = name;
        Jql = jql;
        IsBuiltIn = isBuiltIn;
    }
}
