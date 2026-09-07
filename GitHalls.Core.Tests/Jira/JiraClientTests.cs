using System.Net;
using System.Text;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraClientTests
{
    private static readonly JiraCredentials Credentials =
        new(new Uri("https://acme.atlassian.net"), "me@acme.com", "t0ken");

    /// <summary>Answers every request with one canned response, and records what was asked.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Action<HttpResponseMessage>? _decorate;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body, Action<HttpResponseMessage>? decorate = null)
        {
            _status = status;
            _body = body;
            _decorate = decorate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            _decorate?.Invoke(response);
            return response;
        }
    }

    private static (JiraClient Client, StubHandler Handler) ClientFor(
        HttpStatusCode status, string body, Action<HttpResponseMessage>? decorate = null)
    {
        var handler = new StubHandler(status, body, decorate);
        return (new JiraClient(Credentials, new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task SearchAsync_ReadsTheFieldsTheSidebarShows()
    {
        const string body = """
        {
          "issues": [
            {
              "key": "SWEB-6832",
              "fields": {
                "summary": "Kanban do WhatsApp",
                "status": { "name": "In Progress", "statusCategory": { "key": "indeterminate" } },
                "issuetype": { "name": "Story" },
                "priority": { "name": "High" },
                "updated": "2026-08-07T14:02:11.123-0300"
              }
            }
          ]
        }
        """;

        var (client, handler) = ClientFor(HttpStatusCode.OK, body);

        var issues = await client.SearchAsync("assignee = currentUser()");

        var issue = Assert.Single(issues);
        Assert.Equal("SWEB-6832", issue.Key);
        Assert.Equal("Kanban do WhatsApp", issue.Summary);
        Assert.Equal("In Progress", issue.Status);
        Assert.Equal("indeterminate", issue.StatusCategory);
        Assert.Equal("Story", issue.Type);
        Assert.Equal("High", issue.Priority);
        Assert.False(issue.IsDone);
        Assert.Equal(2026, issue.Updated.Year);

        // The query goes in the body of a POST, and the credentials in the header.
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/search/jql", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Contains("assignee = currentUser()", handler.LastBody);
        Assert.Contains("\"maxResults\":50", handler.LastBody);
    }

    [Fact]
    public async Task SearchAsync_FillsInWhatAnIssueLeavesOut()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"issues":[{"key":"ABC-1","fields":{}}]}""");

        var issue = Assert.Single(await client.SearchAsync("x"));

        Assert.Equal("ABC-1", issue.Summary);   // no summary: the key still names the row
        Assert.Equal("—", issue.Status);
        Assert.Equal("Task", issue.Type);
        Assert.Null(issue.Priority);
    }

    [Fact]
    public async Task SearchAsync_SkipsAnIssueWithNoKey()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"issues":[{"fields":{"summary":"orphan"}}]}""");

        Assert.Empty(await client.SearchAsync("x"));
    }

    [Fact]
    public async Task MyselfAsync_NamesTheAccountTheCredentialsBelongTo()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"accountId":"5f2","displayName":"Erikson"}""");

        var account = await client.MyselfAsync();

        Assert.Equal("5f2", account.AccountId);
        Assert.Equal("Erikson", account.DisplayName);
        Assert.Equal("/rest/api/3/myself", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task MyselfAsync_FallsBackToTheEmailWhenJiraOmitsTheName()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"accountId":"5f2"}""");

        Assert.Equal("me@acme.com", (await client.MyselfAsync()).DisplayName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Send_TreatsBothRejectionsAsBadCredentials(HttpStatusCode status)
    {
        var (client, _) = ClientFor(status, "{}");

        var error = await Assert.ThrowsAsync<JiraException>(() => client.MyselfAsync());
        Assert.Equal(JiraFailure.Unauthorized, error.Failure);
    }

    [Fact]
    public async Task Send_CarriesTheRetryDelayOfARateLimit()
    {
        var (client, _) = ClientFor(HttpStatusCode.TooManyRequests, "{}",
            response => response.Headers.Add("Retry-After", "30"));

        var error = await Assert.ThrowsAsync<JiraException>(() => client.SearchAsync("x"));

        Assert.Equal(JiraFailure.RateLimited, error.Failure);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task Send_RepeatsWhatJiraSaidWasWrong()
    {
        // A bad JQL is the common case, and Jira explains it precisely.
        var (client, _) = ClientFor(HttpStatusCode.BadRequest,
            """{"errorMessages":["Field 'assigne' does not exist."]}""");

        var error = await Assert.ThrowsAsync<JiraException>(() => client.SearchAsync("assigne = x"));

        Assert.Equal(JiraFailure.Http, error.Failure);
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public async Task Send_ReportsAnUnexpectedShapeRatherThanCrashing()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, """{"nothing":"useful"}""");

        var error = await Assert.ThrowsAsync<JiraException>(() => client.SearchAsync("x"));
        Assert.Equal(JiraFailure.MalformedResponse, error.Failure);
    }

    [Fact]
    public void BrowseUrl_PointsAtTheIssueOnTheSite()
    {
        var client = new JiraClient(Credentials);

        Assert.Equal("https://acme.atlassian.net/browse/SWEB-6832", client.BrowseUrl("SWEB-6832").AbsoluteUri);
    }
}
