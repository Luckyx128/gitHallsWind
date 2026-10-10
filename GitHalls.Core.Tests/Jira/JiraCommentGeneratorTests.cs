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
        Assert.Equal(string.Empty, JiraCommentGenerator.GeneratePostPushComment(Array.Empty<Commit>()));
    }

    [Fact]
    public void GeneratePostPushComment_FormatsSingleCommit()
    {
        var commits = new[]
        {
            new Commit("abcdef123", "Author", "author@test.com", DateTimeOffset.Now, "feat: one")
        };

        var expected = "Pushed 1 commit:\n\n* abcdef1 - feat: one";
        
        Assert.Equal(expected, JiraCommentGenerator.GeneratePostPushComment(commits).Replace("\r\n", "\n"));
    }

    [Fact]
    public void GeneratePostPushComment_FormatsMultipleCommits()
    {
        var commits = new[]
        {
            new Commit("abcdef123", "Author", "author@test.com", DateTimeOffset.Now, "feat: one"),
            new Commit("987654321", "Author", "author@test.com", DateTimeOffset.Now, "fix: two\n\nDetails")
        };

        var expected = "Pushed 2 commits:\n\n* abcdef1 - feat: one\n* 9876543 - fix: two";
        
        Assert.Equal(expected, JiraCommentGenerator.GeneratePostPushComment(commits).Replace("\r\n", "\n"));
    }
}
