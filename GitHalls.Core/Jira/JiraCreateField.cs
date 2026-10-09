using System.Text.Json;

namespace GitHalls.Core.Jira;

/// <summary>
/// One field of the create screen for a project and issue type — what a "new
/// issue" form has to render, and which of them it has to insist on.
/// </summary>
public sealed record JiraCreateField(string Key, string Name)
{
    public bool Required { get; init; }
    public JiraFieldKind Kind { get; init; } = JiraFieldKind.String;
    public IReadOnlyList<JiraFieldOption> Allowed { get; init; } = Array.Empty<JiraFieldOption>();

    /// <summary>A field with a default fills itself, so it is not asked for.</summary>
    public bool HasDefault { get; init; }

    /// <summary>Where Jira says candidates can be searched (the Team field, user pickers).</summary>
    public string? AutoCompleteUrl { get; init; }

    /// <summary>What the form has to ask for: required, and with nothing Jira would fill in.</summary>
    public bool NeedsInput => Required && !HasDefault;

    /// <summary>Reads one entry of createmeta's <c>fields</c>; null when it has no id.</summary>
    public static JiraCreateField? Parse(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return null;

        var key = Str(raw, "fieldId") ?? Str(raw, "key");
        if (string.IsNullOrEmpty(key)) return null;

        var allowed = new List<JiraFieldOption>();
        if (raw.TryGetProperty("allowedValues", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (ParseOption(value) is { } option) allowed.Add(option);
            }
        }

        raw.TryGetProperty("schema", out var schema);
        return new JiraCreateField(key, Str(raw, "name") ?? key)
        {
            Required = Bool(raw, "required"),
            Kind = KindOf(schema, allowed.Count > 0),
            Allowed = allowed,
            HasDefault = Bool(raw, "hasDefaultValue"),
            AutoCompleteUrl = Str(raw, "autoCompleteUrl")
        };
    }

    /// <summary>
    /// Allowed values and picker answers name their label under different keys,
    /// and spell the id as a string or a number.
    /// </summary>
    public static JiraFieldOption? ParseOption(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object) return null;

        string? id = null;
        foreach (var name in new[] { "id", "teamId" })
        {
            if (!raw.TryGetProperty(name, out var value)) continue;
            id = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            if (id != null) break;
        }

        var label = new[] { "name", "value", "title", "displayName", "label" }
            .Select(name => Str(raw, name))
            .FirstOrDefault(text => !string.IsNullOrEmpty(text));

        return id == null || label == null ? null : new JiraFieldOption(id, label);
    }

    private static JiraFieldKind KindOf(JsonElement schema, bool hasAllowedValues)
    {
        if (schema.ValueKind != JsonValueKind.Object) return hasAllowedValues ? JiraFieldKind.Option : JiraFieldKind.String;

        var type = Str(schema, "type") ?? string.Empty;
        var items = Str(schema, "items");
        var system = Str(schema, "system");
        var custom = Str(schema, "custom") ?? string.Empty;

        if (system == "timetracking" || type == "timetracking") return JiraFieldKind.TimeTracking;
        if (type == "team" || custom.Contains("atlassian-team") || custom.Contains("teams-custom-field-team"))
        {
            return JiraFieldKind.Team;
        }

        return type switch
        {
            // v3 takes rich-text fields as ADF: description, environment and
            // paragraph custom fields.
            "string" => system is "description" or "environment" || custom.EndsWith(":textarea", StringComparison.Ordinal)
                ? JiraFieldKind.Adf
                : JiraFieldKind.String,
            "number" => JiraFieldKind.Number,
            "date" => JiraFieldKind.Date,
            "datetime" => JiraFieldKind.DateTime,
            "user" => JiraFieldKind.User,
            "priority" => JiraFieldKind.Priority,
            "option" => JiraFieldKind.Option,
            "array" => items switch
            {
                "string" => JiraFieldKind.Labels,
                "component" => JiraFieldKind.Components,
                "user" => JiraFieldKind.UserList,
                "option" or "version" or "group" or "project" => JiraFieldKind.MultiOption,
                _ => hasAllowedValues ? JiraFieldKind.MultiOption : JiraFieldKind.Other
            },
            _ => JiraFieldKind.Other
        };
    }

    private static string? Str(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement raw, string name) =>
        raw.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
