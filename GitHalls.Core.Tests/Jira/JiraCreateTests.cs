using System.Net;
using System.Text;
using System.Text.Json;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

/// <summary>
/// Creating an issue the way a project with required fields needs it: the
/// create screen read from createmeta, the extra fields in Jira's shape, and
/// the new issue moved out of the backlog into the active sprint.
/// </summary>
public class JiraCreateTests
{
    private static readonly JiraCredentials Credentials =
        new(new Uri("https://acme.atlassian.net"), "me@acme.com", "t0ken");

    /// <summary>Answers by path, and records every request in order.</summary>
    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Body)> _route;

        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

        public RoutedHandler(Func<string, (HttpStatusCode, string)> route) => _route = route;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, body));

            var (status, answer) = _route(request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static (JiraClient Client, RoutedHandler Handler) ClientFor(Func<string, (HttpStatusCode, string)> route)
    {
        var handler = new RoutedHandler(route);
        return (new JiraClient(Credentials, new HttpClient(handler)), handler);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // MARK: - The create payload

    [Fact]
    public async Task CreateIssueAsync_LeavesUnsetFieldsOutInsteadOfSendingNull()
    {
        var (client, handler) = ClientFor(_ => (HttpStatusCode.Created, """{"key":"SWEB-7"}"""));

        var key = await client.CreateIssueAsync(new JiraIssueCreateParameters("SWEB", "Do it", "Task"));

        Assert.Equal("SWEB-7", key);
        var body = handler.Requests.Single().Body!;
        Assert.DoesNotContain("null", body);
        Assert.DoesNotContain("priority", body);
        Assert.DoesNotContain("labels", body);
        Assert.DoesNotContain("assignee", body);
        Assert.DoesNotContain("description", body);
        Assert.Contains("\"issuetype\":{\"name\":\"Task\"}", body);
    }

    [Fact]
    public async Task CreateIssueAsync_PrefersTheTypeIdAndWritesTheExtraFieldsBesideTheNamedOnes()
    {
        var (client, handler) = ClientFor(_ => (HttpStatusCode.Created, """{"key":"SWEB-8"}"""));

        await client.CreateIssueAsync(new JiraIssueCreateParameters("SWEB", "Do it", "Task")
        {
            IssueTypeId = "10002",
            Description = JiraAdf.FromPlainText("Why"),
            ExtraFields = new Dictionary<string, JsonElement>
            {
                ["customfield_10001"] = Json("\"team-42\""),
                ["timetracking"] = Json("""{"originalEstimate":"2h 30m"}""")
            }
        });

        using var sent = JsonDocument.Parse(handler.Requests.Single().Body!);
        var fields = sent.RootElement.GetProperty("fields");
        Assert.Equal("10002", fields.GetProperty("issuetype").GetProperty("id").GetString());
        Assert.False(fields.GetProperty("issuetype").TryGetProperty("name", out _));
        Assert.Equal("team-42", fields.GetProperty("customfield_10001").GetString());
        Assert.Equal("2h 30m", fields.GetProperty("timetracking").GetProperty("originalEstimate").GetString());
        Assert.Equal("doc", fields.GetProperty("description").GetProperty("type").GetString());
    }

    // MARK: - createmeta

    [Fact]
    public async Task GetCreateFieldsAsync_ReadsWhatTheSwebScreenRequires()
    {
        const string body = """
        { "fields": [
          { "fieldId": "summary", "name": "Summary", "required": true, "schema": { "type": "string", "system": "summary" } },
          { "fieldId": "description", "name": "Description", "required": true, "schema": { "type": "string", "system": "description" } },
          { "fieldId": "customfield_10001", "name": "Team", "required": true,
            "schema": { "type": "team", "custom": "com.atlassian.jira.plugin.system.customfieldtypes:atlassian-team" },
            "autoCompleteUrl": "https://acme.atlassian.net/rest/teams/1.0/teams/find?query=" },
          { "fieldId": "timetracking", "name": "Time tracking", "required": true, "schema": { "type": "timetracking", "system": "timetracking" } },
          { "fieldId": "priority", "name": "Priority", "required": true, "hasDefaultValue": true, "schema": { "type": "priority" },
            "allowedValues": [ { "id": "3", "name": "Medium" } ] },
          { "fieldId": "customfield_2", "name": "Area", "required": true, "schema": { "type": "option" },
            "allowedValues": [ { "id": 7, "value": "Front" } ] }
        ] }
        """;
        var (client, handler) = ClientFor(_ => (HttpStatusCode.OK, body));

        var fields = await client.GetCreateFieldsAsync("SWEB", "10002");

        Assert.Equal("/rest/api/3/issue/createmeta/SWEB/issuetypes/10002", handler.Requests.Single().PathAndQuery.Split('?')[0]);
        var dynamic = JiraCreateFieldValue.DynamicFields(fields);
        Assert.Equal(new[] { "customfield_10001", "timetracking", "customfield_2" }, dynamic.Select(f => f.Key));
        Assert.Equal(JiraFieldInput.Team, JiraCreateFieldValue.InputFor(dynamic[0]));
        Assert.Equal(JiraFieldInput.Duration, JiraCreateFieldValue.InputFor(dynamic[1]));
        Assert.Equal(new JiraFieldOption("7", "Front"), Assert.Single(dynamic[2].Allowed));
        Assert.Equal(JiraFieldKind.Adf, fields.Single(f => f.Key == "description").Kind);
    }

    [Fact]
    public async Task GetIssueTypesAsync_AcceptsBothAnswerShapes()
    {
        var (cloud, _) = ClientFor(_ => (HttpStatusCode.OK, """{"issueTypes":[{"id":"1","name":"Tarefa","subtask":false},{"id":"2","name":"Subtarefa","subtask":true}]}"""));
        var (paged, _) = ClientFor(_ => (HttpStatusCode.OK, """{"values":[{"id":"1","name":"Task"}]}"""));

        var types = await cloud.GetIssueTypesAsync("SWEB");

        Assert.Equal(new[] { false, true }, types.Select(t => t.IsSubtask));
        Assert.Equal("Task", Assert.Single(await paged.GetIssueTypesAsync("SWEB")).Name);
    }

    [Fact]
    public async Task FindTeamsAsync_FillsTheFieldsOwnUrlAndRefusesAnotherHost()
    {
        var (client, handler) = ClientFor(_ => (HttpStatusCode.OK, """[{"id":"team-42","name":"Front"}]"""));

        var teams = await client.FindTeamsAsync("Fro", "https://acme.atlassian.net/rest/teams/1.0/teams/find?query=");

        Assert.Equal(new JiraFieldOption("team-42", "Front"), Assert.Single(teams));
        Assert.EndsWith("query=Fro", handler.Requests.Single().PathAndQuery);
        await Assert.ThrowsAsync<JiraException>(() => client.FindTeamsAsync("x", "https://evil.example/find?query="));
    }

    // MARK: - Values

    [Theory]
    [InlineData("2h 30m", 9000)]
    [InlineData("1d", 28800)]
    [InlineData("1w 1h", 147600)]
    public void Duration_CountsJiraDaysAndWeeks(string text, int seconds) => Assert.Equal(seconds, JiraDuration.Seconds(text));

    [Theory]
    [InlineData("")]
    [InlineData("2 horas")]
    [InlineData("0m")]
    [InlineData("30")]
    public void Duration_RefusesWhatIsNotADuration(string text) => Assert.Null(JiraDuration.Seconds(text));

    [Fact]
    public void FieldValues_TakeTheShapeEachKindNeeds()
    {
        var area = new JiraCreateField("customfield_2", "Area") { Required = true, Kind = JiraFieldKind.Option };
        var estimate = new JiraCreateField("timetracking", "Time tracking") { Required = true, Kind = JiraFieldKind.TimeTracking };
        var points = new JiraCreateField("customfield_3", "Points") { Required = true, Kind = JiraFieldKind.Number };

        Assert.Equal("""{"id":"7"}""", JiraCreateFieldValue.FromText(area, "7")!.Value.GetRawText());
        Assert.Equal("""{"originalEstimate":"2h"}""", JiraCreateFieldValue.FromText(estimate, "2h")!.Value.GetRawText());
        Assert.Equal("Required.", JiraCreateFieldValue.Problem(estimate, " "));
        Assert.Equal("Use a format like 2h 30m.", JiraCreateFieldValue.Problem(estimate, "duas horas"));
        Assert.Equal("Enter a number.", JiraCreateFieldValue.Problem(points, "três"));
        Assert.Null(JiraCreateFieldValue.Problem(points, "3.5"));
        Assert.Equal("""[{"id":"a"},{"id":"b"}]""", JiraCreateFieldValue.FromIds(new[] { "a", "b" })!.Value.GetRawText());
    }

    [Fact]
    public void Adf_KeepsParagraphsAndLineBreaksAndNeverWritesEmptyText()
    {
        var doc = JiraAdf.FromPlainText("First line\nsecond line\n\nNew paragraph");

        var paragraphs = doc.GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, paragraphs.Count);
        var first = paragraphs[0].GetProperty("content").EnumerateArray().Select(n => n.GetProperty("type").GetString());
        Assert.Equal(new[] { "text", "hardBreak", "text" }, first);
        Assert.DoesNotContain("\"text\":\"\"", doc.GetRawText());
    }

    // MARK: - Sprints

    [Fact]
    public async Task GetOpenSprintsAsync_GathersScrumBoardsOnceAndSkipsKanban()
    {
        var (client, handler) = ClientFor(path => path switch
        {
            "/rest/agile/1.0/board" => (HttpStatusCode.OK,
                """{"values":[{"id":1,"type":"scrum"},{"id":2,"type":"kanban"},{"id":3,"type":"scrum"}]}"""),
            "/rest/agile/1.0/board/1/sprint" => (HttpStatusCode.OK, """{"values":[{"id":5,"name":"Sprint 42","state":"active"}]}"""),
            "/rest/agile/1.0/board/3/sprint" => (HttpStatusCode.OK,
                """{"values":[{"id":6,"name":"Sprint 43","state":"future"},{"id":5,"name":"Sprint 42","state":"active"}]}"""),
            _ => (HttpStatusCode.NotFound, "{}")
        });

        var sprints = await client.GetOpenSprintsAsync("SWEB");

        Assert.Equal(new[] { 5, 6 }, sprints.Select(s => s.Id));
        Assert.Equal(5, JiraSprintChoice.DefaultSprint(sprints)!.Id);
        Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery.StartsWith("/rest/agile/1.0/board/2/"));
    }

    [Fact]
    public async Task GetOpenSprintsAsync_ABoardItCannotReadDoesNotHideTheOthers()
    {
        var (client, _) = ClientFor(path => path switch
        {
            "/rest/agile/1.0/board" => (HttpStatusCode.OK, """{"values":[{"id":1,"type":"scrum"},{"id":3,"type":"scrum"}]}"""),
            "/rest/agile/1.0/board/1/sprint" => (HttpStatusCode.Forbidden, "{}"),
            "/rest/agile/1.0/board/3/sprint" => (HttpStatusCode.OK, """{"values":[{"id":9,"name":"S","state":"active"}]}"""),
            _ => (HttpStatusCode.NotFound, "{}")
        });

        Assert.Equal(9, Assert.Single(await client.GetOpenSprintsAsync("SWEB")).Id);
    }

    [Fact]
    public async Task MoveToSprintAsync_PostsTheKeys()
    {
        var (client, handler) = ClientFor(_ => (HttpStatusCode.NoContent, string.Empty));

        await client.MoveToSprintAsync(5, new[] { "SWEB-7" });

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/rest/agile/1.0/sprint/5/issue", request.PathAndQuery);
        Assert.Equal("""{"issues":["SWEB-7"]}""", request.Body);
    }

    [Fact]
    public void DefaultSprint_IsTheActiveOneThatStartedFirst()
    {
        var early = new JiraSprint(1, "A", "active") { StartDate = DateTimeOffset.Parse("2026-10-01T00:00:00Z") };
        var late = new JiraSprint(2, "B", "active") { StartDate = DateTimeOffset.Parse("2026-10-06T00:00:00Z") };

        Assert.Equal(1, JiraSprintChoice.DefaultSprint(new[] { late, new JiraSprint(3, "C", "future"), early })!.Id);
        Assert.Null(JiraSprintChoice.DefaultSprint(new[] { new JiraSprint(3, "C", "future") }));
    }
}
