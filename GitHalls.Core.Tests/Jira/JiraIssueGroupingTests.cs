using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraIssueGroupingTests
{
    private static JiraIssue Issue(string key, string status, string category) =>
        new(key, key, status, category, "Task", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ByStatus_RunsTheColumnsInWorkflowOrder()
    {
        // Rank order interleaves statuses; the board still reads left to right.
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            Issue("A-1", "In Progress", "indeterminate"),
            Issue("A-2", "To Do", "new"),
            Issue("A-3", "In Progress", "indeterminate")
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal("To Do", groups[0].Status);
        Assert.Equal("In Progress", groups[1].Status);
        Assert.Equal(2, groups[1].Count);
    }

    [Fact]
    public void ByStatus_KeepsTheOrderJiraReturnedWithinACategory()
    {
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            Issue("A-1", "In Review", "indeterminate"),
            Issue("A-2", "In Progress", "indeterminate"),
            Issue("A-3", "Selected", "new"),
            Issue("A-4", "To Do", "new")
        });

        Assert.Equal(new[] { "Selected", "To Do", "In Review", "In Progress" }, groups.Select(g => g.Status));
    }

    [Fact]
    public void Filter_KeepsEveryColumnAndOnlyTheMatchingCards()
    {
        var groups = JiraIssueGrouping.ByStatus(new[]
        {
            new JiraIssue("APP-1", "Login screen", "To Do", "new", "Task", null, DateTimeOffset.UnixEpoch),
            new JiraIssue("APP-2", "Kanban board", "To Do", "new", "Task", null, DateTimeOffset.UnixEpoch),
            new JiraIssue("APP-3", "Crash on login", "Done", "done", "Bug", null, DateTimeOffset.UnixEpoch)
        });

        var filtered = JiraIssueGrouping.Filter(groups, "login");

        Assert.Equal(2, filtered.Count);
        Assert.Equal(new[] { "APP-1" }, filtered[0].Issues.Select(i => i.Key));
        Assert.Equal(new[] { "APP-3" }, filtered[1].Issues.Select(i => i.Key));

        // The key matches too, case aside.
        Assert.Equal(new[] { "APP-2" }, JiraIssueGrouping.Filter(groups, "app-2")[0].Issues.Select(i => i.Key));

        // Blank means no filter — the very same list, not a copy.
        Assert.Same(groups, JiraIssueGrouping.Filter(groups, "  "));
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
