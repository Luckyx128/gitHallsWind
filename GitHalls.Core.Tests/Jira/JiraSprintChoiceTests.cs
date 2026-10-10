using System;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraSprintChoiceTests
{
    [Fact]
    public void DefaultSprint_ReturnsNullWhenEmpty()
    {
        Assert.Null(JiraSprintChoice.DefaultSprint(Array.Empty<JiraSprint>()));
    }

    [Fact]
    public void DefaultSprint_ReturnsNullWhenNoneActive()
    {
        var sprints = new[]
        {
            new JiraSprint(1, "Sprint 1", "closed"),
            new JiraSprint(2, "Sprint 2", "future")
        };

        Assert.Null(JiraSprintChoice.DefaultSprint(sprints));
    }

    [Fact]
    public void DefaultSprint_ReturnsActiveSprint()
    {
        var sprints = new[]
        {
            new JiraSprint(1, "Sprint 1", "closed"),
            new JiraSprint(2, "Sprint 2", "active"),
            new JiraSprint(3, "Sprint 3", "future")
        };

        var defaultSprint = JiraSprintChoice.DefaultSprint(sprints);
        Assert.NotNull(defaultSprint);
        Assert.Equal(2, defaultSprint.Id);
    }

    [Fact]
    public void DefaultSprint_WithParallelSprints_ReturnsTheOneThatStartedFirst()
    {
        var sprints = new[]
        {
            new JiraSprint(1, "Sprint 1", "active") { StartDate = DateTimeOffset.Now.AddDays(-2) },
            new JiraSprint(2, "Sprint 2", "active") { StartDate = DateTimeOffset.Now.AddDays(-5) },
            new JiraSprint(3, "Sprint 3", "active") { StartDate = DateTimeOffset.Now.AddDays(-1) }
        };

        var defaultSprint = JiraSprintChoice.DefaultSprint(sprints);
        Assert.NotNull(defaultSprint);
        Assert.Equal(2, defaultSprint.Id);
    }
}
