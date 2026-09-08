using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHalls.Core.Jira;

// The wire shapes, kept apart from the models the app uses. Source-generated
// for the same reason AppSettings is: the app publishes with PublishTrimmed,
// which strips the reflection metadata the default serializer path needs —
// parsing would come back empty in a published build only.

internal sealed class JiraSearchRequest
{
    public string Jql { get; set; } = string.Empty;
    public string[] Fields { get; set; } = Array.Empty<string>();
    public int MaxResults { get; set; }
}

internal sealed class JiraSearchResponse
{
    public List<JiraIssueDto>? Issues { get; set; }
}

internal sealed class JiraIssueDto
{
    public string? Key { get; set; }
    public JiraFieldsDto? Fields { get; set; }
}

internal sealed class JiraFieldsDto
{
    public string? Summary { get; set; }
    public JiraStatusDto? Status { get; set; }

    /// <summary>Jira spells this one all lowercase, unlike every field beside it.</summary>
    [JsonPropertyName("issuetype")]
    public JiraNamedDto? IssueType { get; set; }

    public JiraNamedDto? Priority { get; set; }
    public string? Updated { get; set; }
    public string? Created { get; set; }
    public JiraUserDto? Assignee { get; set; }
    public JiraUserDto? Reporter { get; set; }
    public List<string>? Labels { get; set; }

    /// <summary>
    /// Atlassian Document Format: a JSON tree, not a string. Kept raw here and
    /// flattened by <see cref="JiraAdf"/> — the wire shape has dozens of node
    /// types and no schema worth typing.
    /// </summary>
    public JsonElement? Description { get; set; }
}

internal sealed class JiraUserDto
{
    public string? AccountId { get; set; }
    public string? DisplayName { get; set; }
}

internal sealed class JiraStatusDto
{
    public string? Name { get; set; }
    public JiraStatusCategoryDto? StatusCategory { get; set; }
}

internal sealed class JiraStatusCategoryDto
{
    public string? Key { get; set; }
}

internal sealed class JiraNamedDto
{
    public string? Name { get; set; }
}

internal sealed class JiraTransitionsResponse
{
    public List<JiraTransitionDto>? Transitions { get; set; }
}

internal sealed class JiraTransitionDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }

    /// <summary>Where the move lands. The same shape as an issue's own status, so it reuses it.</summary>
    public JiraStatusDto? To { get; set; }

    /// <summary>Absent means available; Jira only sends it false when it wants to say so.</summary>
    public bool? IsAvailable { get; set; }

    /// <summary>The move opens a form in Jira — it will refuse a bare POST that skips the fields.</summary>
    public bool? HasScreen { get; set; }
}

internal sealed class JiraTransitionRequest
{
    public JiraIdDto Transition { get; set; } = new();
}

internal sealed class JiraIdDto
{
    public string? Id { get; set; }
}

internal sealed class JiraAssigneeRequest
{
    /// <summary>
    /// Written even when null: {"accountId":null} is how Jira spells "unassign",
    /// and a request that leaves the property out is not the same request. The
    /// attribute is redundant today and guards the day someone adds
    /// DefaultIgnoreCondition to the context below.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? AccountId { get; set; }
}

internal sealed class JiraMyselfResponse
{
    public string? AccountId { get; set; }
    public string? DisplayName { get; set; }
}

internal sealed class JiraErrorResponse
{
    public List<string>? ErrorMessages { get; set; }
    public Dictionary<string, string>? Errors { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JiraSearchRequest))]
[JsonSerializable(typeof(JiraSearchResponse))]
[JsonSerializable(typeof(JiraIssueDto))]
[JsonSerializable(typeof(JiraTransitionsResponse))]
[JsonSerializable(typeof(JiraTransitionRequest))]
[JsonSerializable(typeof(JiraAssigneeRequest))]
[JsonSerializable(typeof(JiraMyselfResponse))]
[JsonSerializable(typeof(JiraErrorResponse))]
internal partial class JiraJsonContext : JsonSerializerContext
{
}
