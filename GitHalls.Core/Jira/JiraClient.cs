using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GitHalls.Core.Jira;

/// <summary>
/// The Jira Cloud calls this app makes: who am I, which issues match a JQL
/// query, everything about one issue, which moves an issue can make, and the
/// two writes — move it, assign it. Nothing here caches or retries — the view
/// model decides when to ask, and a rate limit comes back as an error the user
/// can read.
/// </summary>
public sealed class JiraClient
{
    private const string SearchPath = "/rest/api/3/search/jql";
    private const string MyselfPath = "/rest/api/3/myself";
    private const string IssuePath = "/rest/api/3/issue/";
    private const string CreateMetaPath = "/rest/api/3/issue/createmeta/";
    private const string AgilePath = "/rest/agile/1.0";

    /// <summary>Only what a card renders; asking for everything costs Jira time it doesn't need to spend.</summary>
    private static readonly string[] CardFields = { "summary", "status", "issuetype", "priority", "updated", "assignee" };

    /// <summary>What the detail window shows on top of the card.</summary>
    private static readonly string[] DetailFields =
    {
        "summary", "status", "issuetype", "priority", "updated", "created",
        "assignee", "reporter", "labels", "description"
    };

    /// <summary>
    /// One connection pool for the process. Credentials go on each request, not
    /// on the client, so the same pool serves an account change without carrying
    /// the old authorization over.
    /// </summary>
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly JiraCredentials _credentials;
    private readonly HttpClient _http;

    public JiraClient(JiraCredentials credentials, HttpClient? httpClient = null)
    {
        _credentials = credentials;
        _http = httpClient ?? SharedClient;
    }

    /// <summary>Verifies the credentials and says who they belong to.</summary>
    public async Task<JiraAccount> MyselfAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendAsync(Request(HttpMethod.Get, MyselfPath), cancellationToken);

        var me = Deserialize(body, JiraJsonContext.Default.JiraMyselfResponse);
        if (me?.AccountId is not { Length: > 0 } accountId) throw JiraException.Malformed();

        return new JiraAccount(accountId, me.DisplayName ?? _credentials.Email);
    }

    public async Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, int limit = 50, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Post, SearchPath);
        SetJsonBody(request, new JiraSearchRequest { Jql = jql, Fields = CardFields, MaxResults = limit },
                    JiraJsonContext.Default.JiraSearchRequest);

        var body = await SendAsync(request, cancellationToken);

        var response = Deserialize(body, JiraJsonContext.Default.JiraSearchResponse);
        if (response?.Issues == null) throw JiraException.Malformed();

        var issues = new List<JiraIssue>(response.Issues.Count);
        foreach (var raw in response.Issues)
        {
            if (ToIssue(raw) is { } issue) issues.Add(issue);
        }

        return issues;
    }

    /// <summary>One issue with its description and people, for the detail window.</summary>
    public async Task<JiraIssue> GetIssueAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = IssuePath + Uri.EscapeDataString(key) + "?fields=" + string.Join(",", DetailFields);
        var body = await SendAsync(Request(HttpMethod.Get, path), cancellationToken);

        var raw = Deserialize(body, JiraJsonContext.Default.JiraIssueDto);
        return raw == null ? throw JiraException.Malformed() : ToIssue(raw) ?? throw JiraException.Malformed();
    }

    // MARK: - Workflow

    /// <summary>
    /// The moves this issue can make right now. Transition names are the
    /// project's own invention; the target status and its category are the part
    /// that means the same thing in every project.
    /// </summary>
    public async Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string key, CancellationToken cancellationToken = default)
    {
        var body = await SendAsync(Request(HttpMethod.Get, TransitionsPath(key)), cancellationToken);

        var response = Deserialize(body, JiraJsonContext.Default.JiraTransitionsResponse);
        if (response?.Transitions == null) throw JiraException.Malformed();

        var transitions = new List<JiraTransition>(response.Transitions.Count);
        foreach (var raw in response.Transitions)
        {
            if (raw.Id is not { Length: > 0 } id) continue;
            if (raw.IsAvailable == false) continue;

            var name = raw.Name ?? id;
            transitions.Add(new JiraTransition(
                id,
                name,
                raw.To?.Name ?? name,
                raw.To?.StatusCategory?.Key ?? "indeterminate")
            {
                HasScreen = raw.HasScreen ?? false
            });
        }

        return transitions;
    }

    /// <summary>
    /// Moves the issue along one workflow edge. Jira answers 204 with no body:
    /// the status code is the whole answer, which is why nothing here parses
    /// one — handing an empty body to Deserialize would read as malformed.
    /// </summary>
    public async Task TransitionAsync(string key, string transitionId, JiraIssueUpdateParameters? fields = null, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Post, TransitionsPath(key));

        var body = new JiraTransitionRequest { Transition = new JiraIdDto { Id = transitionId } };
        if (fields != null)
        {
            body.Fields = new JiraUpdateIssueFieldsDto
            {
                Summary = fields.Summary,
                Description = fields.Description,
                IssueType = fields.IssueTypeName != null ? new JiraNamedDto { Name = fields.IssueTypeName } : null,
                Priority = fields.PriorityName != null ? new JiraNamedDto { Name = fields.PriorityName } : null,
                Labels = fields.Labels?.ToList(),
                Assignee = fields.AssigneeAccountId != null ? new JiraUserDto { AccountId = fields.AssigneeAccountId } : null
            };
        }

        SetJsonBody(request, body, JiraJsonContext.Default.JiraTransitionRequest);

        await SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Assigns the issue; a null accountId unassigns it. 204 with no body, for
    /// the same reason as <see cref="TransitionAsync"/>.
    /// </summary>
    public async Task AssignAsync(string key, string? accountId, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Put, IssuePath + Uri.EscapeDataString(key) + "/assignee");
        SetJsonBody(request, new JiraAssigneeRequest { AccountId = accountId },
                    JiraJsonContext.Default.JiraAssigneeRequest);

        await SendAsync(request, cancellationToken);
    }

    /// <summary>Creates a new issue. Returns the key of the created issue.</summary>
    public async Task<string> CreateIssueAsync(JiraIssueCreateParameters parameters, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Post, IssuePath.TrimEnd('/'));

        var body = new JiraCreateIssueRequest
        {
            Fields = new JiraCreateIssueFieldsDto
            {
                Project = new JiraProjectDto { Key = parameters.ProjectKey },
                Summary = parameters.Summary,
                Description = parameters.Description,
                IssueType = parameters.IssueTypeId is { Length: > 0 } typeId
                    ? new JiraRefDto { Id = typeId }
                    : new JiraRefDto { Name = parameters.IssueTypeName },
                Priority = parameters.PriorityName != null ? new JiraNamedDto { Name = parameters.PriorityName } : null,
                Labels = parameters.Labels?.ToList(),
                Assignee = parameters.AssigneeAccountId != null ? new JiraUserDto { AccountId = parameters.AssigneeAccountId } : null,
                Extra = parameters.ExtraFields is { Count: > 0 } extra ? new Dictionary<string, JsonElement>(extra) : null
            }
        };

        SetJsonBody(request, body, JiraJsonContext.Default.JiraCreateIssueRequest);

        var responseBody = await SendAsync(request, cancellationToken);
        var response = Deserialize(responseBody, JiraJsonContext.Default.JiraCreateIssueResponse);

        return response?.Key ?? throw JiraException.Malformed();
    }

    /// <summary>Updates an existing issue. Jira answers 204 with no body.</summary>
    public async Task UpdateIssueAsync(string key, JiraIssueUpdateParameters parameters, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Put, IssuePath + Uri.EscapeDataString(key));

        var body = new JiraUpdateIssueRequest
        {
            Fields = new JiraUpdateIssueFieldsDto
            {
                Summary = parameters.Summary,
                Description = parameters.Description,
                IssueType = parameters.IssueTypeName != null ? new JiraNamedDto { Name = parameters.IssueTypeName } : null,
                Priority = parameters.PriorityName != null ? new JiraNamedDto { Name = parameters.PriorityName } : null,
                Labels = parameters.Labels?.ToList(),
                Assignee = parameters.AssigneeAccountId != null ? new JiraUserDto { AccountId = parameters.AssigneeAccountId } : null
            }
        };

        SetJsonBody(request, body, JiraJsonContext.Default.JiraUpdateIssueRequest);

        await SendAsync(request, cancellationToken);
    }

    // MARK: - What a create form needs

    /// <summary>Projects the user may browse, by name. The first 100: a picker longer than that wants a search box.</summary>
    public async Task<IReadOnlyList<JiraProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendAsync(Request(HttpMethod.Get, "/rest/api/3/project/search?orderBy=name&maxResults=100"), cancellationToken);

        return Values(body, "values")
            .Select(raw => (Id: Text(raw, "id"), Key: Text(raw, "key"), Name: Text(raw, "name")))
            .Where(project => project.Id != null && project.Key != null)
            .Select(project => new JiraProject(project.Id!, project.Key!, project.Name ?? project.Key!))
            .ToList();
    }

    /// <summary>What can be created in the project, subtask types included and flagged.</summary>
    public async Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        var path = CreateMetaPath + Uri.EscapeDataString(projectKey) + "/issuetypes?maxResults=100";
        var body = await SendAsync(Request(HttpMethod.Get, path), cancellationToken);

        // Cloud answers `issueTypes`; the paged shape of newer docs says `values`.
        return Values(body, "issueTypes", "values")
            .Select(raw => (Id: Text(raw, "id"), Name: Text(raw, "name"),
                            Subtask: raw.TryGetProperty("subtask", out var flag) && flag.ValueKind == JsonValueKind.True))
            .Where(type => type.Id != null)
            .Select(type => new JiraIssueType(type.Id!, type.Name ?? type.Id!, type.Subtask))
            .ToList();
    }

    /// <summary>The fields of the create screen for this project and type, required ones flagged.</summary>
    public async Task<IReadOnlyList<JiraCreateField>> GetCreateFieldsAsync(string projectKey, string issueTypeId,
                                                                           CancellationToken cancellationToken = default)
    {
        var path = CreateMetaPath + Uri.EscapeDataString(projectKey) + "/issuetypes/" + Uri.EscapeDataString(issueTypeId) + "?maxResults=200";
        var body = await SendAsync(Request(HttpMethod.Get, path), cancellationToken);

        return Values(body, "fields", "values")
            .Select(JiraCreateField.Parse)
            .OfType<JiraCreateField>()
            .ToList();
    }

    /// <summary>
    /// Teams matching <paramref name="query"/>, for the Team field. Jira says
    /// where to search in the field's own createmeta entry; the fallback is the
    /// path Jira's own picker has used, not part of the public reference. The
    /// URL must be on the Jira site: it is sent the credentials.
    /// </summary>
    public async Task<IReadOnlyList<JiraFieldOption>> FindTeamsAsync(string query, string? autoCompleteUrl = null,
                                                                     CancellationToken cancellationToken = default)
    {
        var text = query.Trim();
        HttpRequestMessage request;

        if (autoCompleteUrl is { Length: > 0 })
        {
            var url = SuggestionUrl(autoCompleteUrl, text);
            if (url == null || !string.Equals(url.Host, _credentials.Site.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw JiraException.Malformed();
            }
            request = Request(HttpMethod.Get, url);
        }
        else
        {
            request = Request(HttpMethod.Get, "/rest/teams/1.0/teams/find?query=" + Uri.EscapeDataString(text));
        }

        var body = await SendAsync(request, cancellationToken);
        return Values(body, "teams", "results", "values", "suggestions", "items")
            .Select(JiraCreateField.ParseOption)
            .OfType<JiraFieldOption>()
            .ToList();
    }

    /// <summary>The autocomplete URL with the query filled in: many end in <c>query=</c>.</summary>
    public static Uri? SuggestionUrl(string template, string query)
    {
        var encoded = Uri.EscapeDataString(query);
        if (template.EndsWith('=')) return Uri.TryCreate(template + encoded, UriKind.Absolute, out var direct) ? direct : null;

        var joiner = template.Contains('?') ? "&" : "?";
        return Uri.TryCreate(template + joiner + "query=" + encoded, UriKind.Absolute, out var url) ? url : null;
    }

    // MARK: - Sprints

    /// <summary>
    /// Active and future sprints of the project's scrum boards, active first.
    /// An issue created through the API lands in the backlog; these are where
    /// it can go instead. A board the user cannot read must not hide the others.
    /// </summary>
    public async Task<IReadOnlyList<JiraSprint>> GetOpenSprintsAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        var boards = await SendAsync(Request(HttpMethod.Get, AgilePath + "/board?projectKeyOrId=" + Uri.EscapeDataString(projectKey)),
                                     cancellationToken);

        var sprints = new List<JiraSprint>();
        foreach (var board in Values(boards, "values"))
        {
            // A kanban board has no sprints and answers 400 when asked.
            if (Text(board, "type") != "scrum" || !board.TryGetProperty("id", out var id) || !id.TryGetInt32(out var boardId)) continue;

            string body;
            try
            {
                body = await SendAsync(Request(HttpMethod.Get, AgilePath + $"/board/{boardId}/sprint?state=active,future"), cancellationToken);
            }
            catch (JiraException)
            {
                continue;
            }

            foreach (var raw in Values(body, "values"))
            {
                if (!raw.TryGetProperty("id", out var sprintId) || !sprintId.TryGetInt32(out var number)) continue;
                sprints.Add(new JiraSprint(number, Text(raw, "name") ?? $"Sprint {number}", Text(raw, "state") ?? "future")
                {
                    StartDate = DateTimeOffset.TryParse(Text(raw, "startDate"), out var start) ? start : null
                });
            }
        }

        return JiraSprintChoice.Ordered(sprints);
    }

    /// <summary>Files the issues in the sprint. 204, no body.</summary>
    public async Task MoveToSprintAsync(int sprintId, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var request = Request(HttpMethod.Post, AgilePath + $"/sprint/{sprintId}/issue");
        SetJsonBody(request, new JiraMoveToSprintRequest { Issues = keys.ToList() }, JiraJsonContext.Default.JiraMoveToSprintRequest);

        await SendAsync(request, cancellationToken);
    }

    /// <summary>Where a human opens this issue.</summary>
    public Uri BrowseUrl(string key) => new(SiteRoot + "/browse/" + Uri.EscapeDataString(key));

    // MARK: - Transport

    private string SiteRoot => _credentials.Site.AbsoluteUri.TrimEnd('/');

    private static string TransitionsPath(string key) => IssuePath + Uri.EscapeDataString(key) + "/transitions";

    private HttpRequestMessage Request(HttpMethod method, string path) =>
        // Concatenated rather than composed with Uri: a site given with a path
        // ("https://host/jira") keeps it, which relative composition would drop.
        Request(method, new Uri(SiteRoot + path));

    private HttpRequestMessage Request(HttpMethod method, Uri url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _credentials.AuthorizationHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// The list in a body that is either an array itself or wraps one under the
    /// first of <paramref name="keys"/> present. Read through JsonDocument rather
    /// than typed DTOs: these shapes vary by endpoint and Jira version, and
    /// JsonDocument needs no reflection, so trimming leaves it intact.
    /// </summary>
    private static IReadOnlyList<JsonElement> Values(string body, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(body)) throw JiraException.Malformed();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw JiraException.Malformed();
        }

        using (document)
        {
            var root = document.RootElement;
            var list = root.ValueKind == JsonValueKind.Array
                ? root
                : keys.Select(key => root.TryGetProperty(key, out var value) ? value : default)
                      .FirstOrDefault(value => value.ValueKind == JsonValueKind.Array);

            if (list.ValueKind != JsonValueKind.Array) throw JiraException.Malformed();
            return list.EnumerateArray().Select(element => element.Clone()).ToList();
        }
    }

    private static string? Text(JsonElement raw, string name) =>
        raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    /// <summary>The body's media type lives here rather than at each call site, which is what Jira rejects a request for missing.</summary>
    private static void SetJsonBody<T>(HttpRequestMessage request, T payload, JsonTypeInfo<T> typeInfo)
    {
        request.Content = new StringContent(JsonSerializer.Serialize(payload, typeInfo), Encoding.UTF8, "application/json");
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No network, bad DNS, TLS refused, or the 20s timeout expired.
            throw new JiraException(JiraFailure.Unreachable, $"Could not reach {_credentials.Site.Host}.", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode) return body;

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => JiraException.Unauthorized(),
                HttpStatusCode.TooManyRequests => JiraException.RateLimited(RetryAfter(response)),
                _ => JiraException.Http((int)response.StatusCode, ErrorMessage(body))
            };
        }
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var seconds = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        return seconds is { TotalSeconds: > 0 } wait ? wait : TimeSpan.FromSeconds(60);
    }

    /// <summary>Jira states the reason in one of two shapes; either beats "responded with 400".</summary>
    private static string? ErrorMessage(string body)
    {
        var error = Deserialize(body, JiraJsonContext.Default.JiraErrorResponse);
        if (error == null) return null;

        if (error.ErrorMessages is { Count: > 0 } messages) return messages[0];
        if (error.Errors is { Count: > 0 } errors) return errors.Values.First();
        return null;
    }

    private static T? Deserialize<T>(string body, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            return JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JiraIssue? ToIssue(JiraIssueDto raw)
    {
        if (raw.Key is not { Length: > 0 } key) return null;

        var fields = raw.Fields;
        return new JiraIssue(
            key,
            fields?.Summary ?? key,
            fields?.Status?.Name ?? "—",
            fields?.Status?.StatusCategory?.Key ?? "indeterminate",
            fields?.IssueType?.Name ?? "Task",
            fields?.Priority?.Name,
            JiraTimestamp.Parse(fields?.Updated))
        {
            AssigneeName = fields?.Assignee?.DisplayName,
            AssigneeAccountId = fields?.Assignee?.AccountId,
            ReporterName = fields?.Reporter?.DisplayName,
            Created = JiraTimestamp.Parse(fields?.Created),
            Labels = fields?.Labels ?? new List<string>(),
            // Null (never asked) is not the same as empty (asked, and there is
            // none): the detail window tells the two apart.
            Description = fields?.Description == null ? null : JiraAdf.ToPlainText(fields.Description)
        };
    }
}
