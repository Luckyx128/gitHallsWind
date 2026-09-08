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
[JsonSerializable(typeof(JiraMyselfResponse))]
[JsonSerializable(typeof(JiraErrorResponse))]
internal partial class JiraJsonContext : JsonSerializerContext
{
}
