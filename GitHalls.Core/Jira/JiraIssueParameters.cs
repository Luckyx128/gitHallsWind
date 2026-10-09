using System.Text.Json;

namespace GitHalls.Core.Jira;

public sealed record JiraIssueCreateParameters(
    string ProjectKey,
    string Summary,
    string IssueTypeName)
{
    /// <summary>Preferred over the name when known: names are translated per site ("Task", "Tarefa"), ids are not.</summary>
    public string? IssueTypeId { get; init; }

    /// <summary>Required fields beyond the named ones, by field id, already in Jira's shape (see <see cref="JiraCreateFieldValue"/>).</summary>
    public IReadOnlyDictionary<string, JsonElement>? ExtraFields { get; init; }

    public JsonElement? Description { get; init; }
    public string? PriorityName { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
    public string? AssigneeAccountId { get; init; }
}

public sealed record JiraIssueUpdateParameters
{
    public string? Summary { get; init; }
    public JsonElement? Description { get; init; }
    public string? IssueTypeName { get; init; }
    public string? PriorityName { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
    public string? AssigneeAccountId { get; init; }
}
