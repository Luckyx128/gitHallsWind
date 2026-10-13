using System;
using GitHalls.Core.Jira;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraCommentGeneratorTests
{
    [Fact]
    public void GeneratePostPushComment_EmptyListReturnsEmptyString()
    {
        Assert.Equal(string.Empty, JiraCommentGenerator.GeneratePostPushComment(Array.Empty<Commit>(), "main"));
    }

    [Fact]
    public void GeneratePostPushComment_FormatsSingleCommitWithBranchAndRepo()
    {
        var commits = new[]
        {
            new Commit("abcdef123", "Author", "author@test.com", DateTimeOffset.Now, "feat: one", url: "https://github.com/org/repo/commit/abcdef123")
        };

        var expected = "**1 commit enviado para main** em my-repo\n\n- [abcdef1](https://github.com/org/repo/commit/abcdef123) feat: one\n\n_via GitHalls_";
        
        Assert.Equal(expected, JiraCommentGenerator.GeneratePostPushComment(commits, "main", "my-repo"));
    }

    [Fact]
    public void GeneratePostPushComment_FormatsMultipleCommitsWithoutRepo()
    {
        var commits = new[]
        {
            new Commit("abcdef123", "Author", "author@test.com", DateTimeOffset.Now, "feat: one"),
            new Commit("987654321", "Author", "author@test.com", DateTimeOffset.Now, "fix: two\n\nDetails")
        };

        var expected = "**2 commits enviados para feature/xyz**\n\n- [abcdef1]() feat: one\n- [9876543]() fix: two\n\n_via GitHalls_";
        
        Assert.Equal(expected, JiraCommentGenerator.GeneratePostPushComment(commits, "feature/xyz"));
    }
}
