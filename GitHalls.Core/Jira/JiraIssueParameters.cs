using System.Text.Json;

namespace GitHalls.Core.Jira;

public sealed record JiraIssueCreateParameters(
    string ProjectKey,
    string Summary,
    string IssueTypeName)
{
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
