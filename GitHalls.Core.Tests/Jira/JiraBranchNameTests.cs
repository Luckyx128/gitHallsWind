using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraBranchNameTests
{
    private static JiraIssue Issue(string key, string type, string summary) =>
        new(key, summary, "To Do", "new", type, null, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("Bug", "SWEB-12903", "fix-SWEB-12903")]
    [InlineData("Story", "SWEB-12903", "feature-SWEB-12903")]
    [InlineData("Task", "SWEB-12903", "chore-SWEB-12903")]
    public void Suggest_NamesTheBranchAfterTheTypeAndTheKey(string type, string key, string expected)
    {
        Assert.Equal(expected, JiraBranchName.Suggest(Issue(key, type, "Anything at all")));
    }

    [Fact]
    public void Suggest_KeepsTheKeyExactlyAsJiraSpellsIt()
    {
        Assert.Equal("feature-SWEB-12903", JiraBranchName.SuggestFor("SWEB-12903", "Story"));
    }

    [Fact]
    public void Suggest_DropsTheSummaryEntirely()
    {
        var issue = Issue("ABC-1", "Task", "A summary long enough to have been a slug before");
        Assert.Equal("chore-ABC-1", JiraBranchName.Suggest(issue));
    }

    [Theory]
    [InlineData("Spike")]
    [InlineData("")]
    [InlineData(null)]
    public void Suggest_UsesFeatureForAnIssueTypeItDoesNotKnow(string? type)
    {
        Assert.Equal("feature-ABC-9", JiraBranchName.SuggestFor("ABC-9", type));
    }

    [Fact]
    public void Suggest_CollapsesWhatGitWouldRefuseInAKey()
    {
        Assert.Equal("feature-ABC-9", JiraBranchName.SuggestFor("  ABC 9  ", "Story"));
    }

    [Fact]
    public void Suggest_FallsBackToTheTypeAloneWhenNothingSurvivesTheKey()
    {
        Assert.Equal("fix", JiraBranchName.SuggestFor("~~~", "Bug"));
    }
}
