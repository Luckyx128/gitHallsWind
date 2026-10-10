using GitHalls.Core.GitHub;
using Xunit;

namespace GitHalls.Core.Tests.GitHub;

public class PullRequestRuleEngineTests
{
    [Fact]
    public void EvaluateJiraStatus_ReturnsNullWhenNoRulesMatch()
    {
        var engine = new PullRequestRuleEngine(new[]
        {
            new PullRequestRule("^main$", "Done")
        });

        Assert.Null(engine.EvaluateJiraStatus("develop"));
    }

    [Fact]
    public void EvaluateJiraStatus_ReturnsJiraStatusOnFirstMatch()
    {
        var engine = new PullRequestRuleEngine(new[]
        {
            new PullRequestRule("^develop$", "Code Review"),
            new PullRequestRule(".*", "In Progress")
        });

        Assert.Equal("Code Review", engine.EvaluateJiraStatus("develop"));
        Assert.Equal("In Progress", engine.EvaluateJiraStatus("feature/branch"));
    }

    [Fact]
    public void EvaluateJiraStatus_IgnoresInvalidRegex()
    {
        var engine = new PullRequestRuleEngine(new[]
        {
            new PullRequestRule("[invalid", "Testing"),
            new PullRequestRule("^main$", "Done")
        });

        Assert.Equal("Done", engine.EvaluateJiraStatus("main"));
    }

    [Fact]
    public void EvaluateJiraStatus_ReturnsNullForEmptyBranchName()
    {
        var engine = new PullRequestRuleEngine(new[]
        {
            new PullRequestRule(".*", "In Progress")
        });

        Assert.Null(engine.EvaluateJiraStatus(""));
        Assert.Null(engine.EvaluateJiraStatus(null!));
    }
}
