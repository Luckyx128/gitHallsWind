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
        private readonly Queue<(HttpStatusCode Status, string Body, Action<HttpResponseMessage>? Decorate)> _responses;
        
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();

        public HttpRequestMessage? LastRequest => Requests.LastOrDefault();
        public string? LastBody => Bodies.LastOrDefault();

        public StubHandler(HttpStatusCode status, string body, Action<HttpResponseMessage>? decorate = null)
        {
            _responses = new Queue<(HttpStatusCode, string, Action<HttpResponseMessage>?)>();
            _responses.Enqueue((status, body, decorate));
        }

        public StubHandler(IEnumerable<(HttpStatusCode Status, string Body, Action<HttpResponseMessage>? Decorate)> responses)
        {
            _responses = new Queue<(HttpStatusCode, string, Action<HttpResponseMessage>?)>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, body, decorate) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();

            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            decorate?.Invoke(response);
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

        var error = await Assert.ThrowsAsync<JiraException>(() => client.MyselfAsync());

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
    public async Task GetIssueAsync_ReadsTheDetailTheWindowShows()
    {
        const string body = """
        {
          "key": "SWEB-6832",
          "fields": {
            "summary": "Kanban do WhatsApp",
            "status": { "name": "Done", "statusCategory": { "key": "done" } },
            "issuetype": { "name": "Story" },
            "priority": { "name": "High" },
            "updated": "2026-08-07T14:02:11.123-0300",
            "created": "2026-08-01T09:00:00.000-0300",
            "assignee": { "accountId": "5f2a1b", "displayName": "Erikson" },
            "reporter": { "displayName": "Ana" },
            "labels": ["mobile", "whatsapp"],
            "description": {
              "type": "doc", "version": 1,
              "content": [
                { "type": "paragraph", "content": [ { "type": "text", "text": "First line." } ] },
                { "type": "bulletList", "content": [
                  { "type": "listItem", "content": [ { "type": "paragraph", "content": [ { "type": "text", "text": "one" } ] } ] }
                ] }
              ]
            }
          }
        }
        """;

        var (client, handler) = ClientFor(HttpStatusCode.OK, body);

        var issue = await client.GetIssueAsync("SWEB-6832");

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/SWEB-6832", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("description", handler.LastRequest.RequestUri.Query);

        Assert.Equal("Erikson", issue.AssigneeName);
        // The id, not the name, is what an assign has to be written back with.
        Assert.Equal("5f2a1b", issue.AssigneeAccountId);
        Assert.Equal("Ana", issue.ReporterName);
        Assert.Equal(new[] { "mobile", "whatsapp" }, issue.Labels);
        Assert.Equal(1, issue.Created.Day);
        Assert.True(issue.IsDone);
        Assert.Equal("First line.\n• one", issue.Description);
    }

    [Fact]
    public async Task SearchAsync_LeavesTheDescriptionUnknownRatherThanEmpty()
    {
        // The search never asks for it; "no description" is only true after a fetch.
        var (client, handler) = ClientFor(HttpStatusCode.OK, """{"issues":[{"key":"ABC-1","fields":{"assignee":{"displayName":"Bo"}}}]}""");

        var issue = Assert.Single(await client.SearchAsync("x"));

        Assert.Null(issue.Description);
        Assert.Equal("Bo", issue.AssigneeName);
        Assert.Contains("\"assignee\"", handler.LastBody);
        Assert.DoesNotContain("description", handler.LastBody);
    }

    // MARK: - Workflow

    private const string TransitionsBody = """
    {
      "transitions": [
        {
          "id": "21",
          "name": "Start progress",
          "to": { "name": "In Progress", "statusCategory": { "key": "indeterminate" } }
        },
        {
          "id": "31",
          "name": "Done",
          "hasScreen": true,
          "to": { "name": "Done", "statusCategory": { "key": "done" } }
        }
      ]
    }
    """;

    [Fact]
    public async Task GetTransitionsAsync_ReadsTheMovesAndWhereTheyLead()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, TransitionsBody);

        var transitions = await client.GetTransitionsAsync("SWEB-6832");

        Assert.Equal(2, transitions.Count);
        Assert.Equal("21", transitions[0].Id);
        Assert.Equal("Start progress", transitions[0].Name);
        Assert.Equal("In Progress", transitions[0].ToStatus);
        Assert.True(transitions[0].LeadsToInProgress);
        Assert.False(transitions[0].HasScreen);
        Assert.True(transitions[1].HasScreen);
        Assert.False(transitions[1].LeadsToInProgress);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/SWEB-6832/transitions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task GetTransitionsAsync_SkipsATransitionWithNoId()
    {
        const string body = """
        { "transitions": [ { "name": "Nameless", "to": { "name": "Done" } }, { "id": "5", "name": "Close" } ] }
        """;

        var (client, _) = ClientFor(HttpStatusCode.OK, body);

        var transition = Assert.Single(await client.GetTransitionsAsync("SWEB-1"));
        Assert.Equal("5", transition.Id);
        // With no "to" of its own, a move is described by its own name.
        Assert.Equal("Close", transition.ToStatus);
    }

    [Fact]
    public async Task GetTransitionsAsync_SkipsOneJiraSaysIsNotAvailable()
    {
        const string body = """
        {
          "transitions": [
            { "id": "9", "name": "Blocked", "isAvailable": false, "to": { "name": "Blocked" } },
            { "id": "10", "name": "Close", "to": { "name": "Done", "statusCategory": { "key": "done" } } }
          ]
        }
        """;

        var (client, _) = ClientFor(HttpStatusCode.OK, body);

        var transition = Assert.Single(await client.GetTransitionsAsync("SWEB-1"));
        Assert.Equal("10", transition.Id);
    }

    [Fact]
    public async Task GetTransitionsAsync_ReportsAnUnexpectedShapeRatherThanCrashing()
    {
        var (client, _) = ClientFor(HttpStatusCode.OK, "{}");

        var error = await Assert.ThrowsAsync<JiraException>(() => client.GetTransitionsAsync("SWEB-1"));
        Assert.Equal(JiraFailure.MalformedResponse, error.Failure);
    }

    // MARK: - Writes

    [Fact]
    public async Task TransitionAsync_PostsTheTransitionIdToTheIssuesTransitions()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.TransitionAsync("SWEB-6832", "31");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/SWEB-6832/transitions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("\"id\":\"31\"", handler.LastBody);
    }

    /// <summary>The 204 has no body, and reading that as a failed parse is the mistake waiting to be made.</summary>
    [Fact]
    public async Task TransitionAsync_AcceptsTheEmptyBodyOfA204()
    {
        var (client, _) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.TransitionAsync("SWEB-1", "5");
    }

    [Fact]
    public async Task TransitionAsync_RepeatsWhatJiraSaidAboutARefusedMove()
    {
        const string body = """
        { "errorMessages": ["Field 'resolution' is required."] }
        """;

        var (client, _) = ClientFor(HttpStatusCode.BadRequest, body);

        var error = await Assert.ThrowsAsync<JiraException>(() => client.TransitionAsync("SWEB-1", "31"));
        Assert.Equal("Field 'resolution' is required.", error.Message);
    }

    [Fact]
    public async Task AssignAsync_PutsTheAccountIdOnTheAssigneeEndpoint()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.AssignAsync("SWEB-6832", "5f2a1b");

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/SWEB-6832/assignee", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"accountId\":\"5f2a1b\"", handler.LastBody);
    }

    /// <summary>Unassigning is an explicit null, not an omitted property.</summary>
    [Fact]
    public async Task AssignAsync_SendsAnExplicitNullToUnassign()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.AssignAsync("SWEB-6832", null);

        Assert.Contains("\"accountId\":null", handler.LastBody);
    }

    [Fact]
    public void BrowseUrl_PointsAtTheIssueOnTheSite()
    {
        var client = new JiraClient(Credentials);

        Assert.Equal("https://acme.atlassian.net/browse/SWEB-6832", client.BrowseUrl("SWEB-6832").AbsoluteUri);
    }

    [Fact]
    public async Task SearchAsync_HandlesPaginationAnd429WaitLogic()
    {
        var responses = new (HttpStatusCode Status, string Body, Action<HttpResponseMessage>? Decorate)[]
        {
            (HttpStatusCode.OK, """{"total": 3, "issues": [{"key": "A-1"}]}""", null),
            (HttpStatusCode.TooManyRequests, "{}", response => response.Headers.Add("Retry-After", "1")),
            (HttpStatusCode.OK, """{"total": 3, "issues": [{"key": "A-2"}, {"key": "A-3"}]}""", null)
        };

        var handler = new StubHandler(responses);
        var client = new JiraClient(Credentials, new HttpClient(handler));

        var issues = await client.SearchAsync("project = A", 3);

        Assert.Equal(3, issues.Count);
        Assert.Equal("A-1", issues[0].Key);
        Assert.Equal("A-2", issues[1].Key);
        Assert.Equal("A-3", issues[2].Key);

        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("\"startAt\"", handler.Bodies[0]);
        Assert.Contains("\"startAt\":1", handler.Bodies[1]);
        Assert.Contains("\"startAt\":1", handler.Bodies[2]);
    }

    [Fact]
    public async Task GetCreateFieldsAsync_CallsCreatemetaEndpoint()
    {
        var (client, handler) = ClientFor(HttpStatusCode.OK, """
        {
          "values": [
            { "fieldId": "summary", "name": "Summary", "required": true, "schema": { "type": "string" } },
            { "fieldId": "description", "name": "Description", "required": false, "schema": { "type": "string" } }
          ]
        }
        """);

        var fields = await client.GetCreateFieldsAsync("PROJ", "10001");

        Assert.Equal(2, fields.Count);
        Assert.Equal("summary", fields[0].Key);
        Assert.True(fields[0].Required);
        Assert.False(fields[1].Required);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/createmeta/PROJ/issuetypes/10001", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UpdateIssueAsync_SendsFieldsToIssueEndpoint()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.UpdateIssueAsync("PROJ-123", new JiraIssueUpdateParameters { Summary = "New Summary" });

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/PROJ-123", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"summary\":\"New Summary\"", handler.LastBody);
    }

    [Fact]
    public async Task TransitionAsync_WithFields_SendsFieldsInRequest()
    {
        var (client, handler) = ClientFor(HttpStatusCode.NoContent, string.Empty);

        await client.TransitionAsync("PROJ-123", "31", new JiraIssueUpdateParameters { Summary = "Updated during transition" });

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/rest/api/3/issue/PROJ-123/transitions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"id\":\"31\"", handler.LastBody);
        Assert.Contains("\"summary\":\"Updated during transition\"", handler.LastBody);
    }
}
