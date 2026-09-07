using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraIssueGroupingTests
{
    private static JiraIssue Issue(string key, string status, string category) =>
        new(key, key, status, category, "Task", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ByStatus_KeepsTheOrderJiraReturned()
    {
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            Issue("A-1", "In Progress", "indeterminate"),
            Issue("A-2", "To Do", "new"),
            Issue("A-3", "In Progress", "indeterminate")
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal("In Progress", groups[0].Status);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal("To Do", groups[1].Status);
    }

    [Fact]
    public void ByStatus_SinksFinishedWorkToTheBottom()
    {
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            Issue("A-1", "Done", "done"),
            Issue("A-2", "To Do", "new"),
            Issue("A-3", "Released", "done"),
            Issue("A-4", "In Review", "indeterminate")
        });

        Assert.Equal(new[] { "To Do", "In Review", "Done", "Released" }, groups.Select(g => g.Status));

        // And a done column arrives closed.
        Assert.False(groups[^1].StartsExpanded);
        Assert.True(groups[0].StartsExpanded);
    }

    [Fact]
    public void ByStatus_IsEmptyForNoIssues()
    {
        Assert.Empty(JiraIssueGrouping.ByStatus(Array.Empty<JiraIssue>()));
    }

    [Fact]
    public void ByStatus_TreatsTwoStatusesOfTheSameCategoryAsTwoColumns()
    {
        // "In Progress" and "In Review" are both indeterminate, and a board still
        // shows them apart.
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            Issue("A-1", "In Progress", "indeterminate"),
            Issue("A-2", "In Review", "indeterminate")
        });

        Assert.Equal(2, groups.Count);
    }
}
