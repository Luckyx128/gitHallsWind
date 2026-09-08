using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GitHalls.Core.Jira;

/// <summary>
/// The three Jira Cloud calls this app makes: who am I, which issues match a
/// JQL query, and everything about one issue. Nothing here caches or retries —
/// the view model decides when to ask, and a rate limit comes back as an error
/// the user can read.
/// </summary>
public sealed class JiraClient
{
    private const string SearchPath = "/rest/api/3/search/jql";
    private const string MyselfPath = "/rest/api/3/myself";
    private const string IssuePath = "/rest/api/3/issue/";

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
        var payload = JsonSerializer.Serialize(
            new JiraSearchRequest { Jql = jql, Fields = CardFields, MaxResults = limit },
            JiraJsonContext.Default.JiraSearchRequest);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

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

    /// <summary>Where a human opens this issue.</summary>
    public Uri BrowseUrl(string key) => new(SiteRoot + "/browse/" + Uri.EscapeDataString(key));

    // MARK: - Transport

    private string SiteRoot => _credentials.Site.AbsoluteUri.TrimEnd('/');

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        // Concatenated rather than composed with Uri: a site given with a path
        // ("https://host/jira") keeps it, which relative composition would drop.
        var request = new HttpRequestMessage(method, SiteRoot + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _credentials.AuthorizationHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
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

    private static T? Deserialize<T>(string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
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
            ReporterName = fields?.Reporter?.DisplayName,
            Created = JiraTimestamp.Parse(fields?.Created),
            Labels = fields?.Labels ?? new List<string>(),
            // Null (never asked) is not the same as empty (asked, and there is
            // none): the detail window tells the two apart.
            Description = fields?.Description == null ? null : JiraAdf.ToPlainText(fields.Description)
        };
    }
}
