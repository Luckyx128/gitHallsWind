using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class GitServiceTests
{
    [Theory]
    [InlineData("https://github.com/user/repo.git", "repo")]
    [InlineData("https://github.com/user/repo", "repo")]
    [InlineData("https://github.com/user/repo/", "repo")]
    [InlineData("https://github.com/user/repo.git/", "repo")]
    [InlineData("git@github.com:user/repo.git", "repo")]
    [InlineData("ssh://git@host:2222/user/repo.git", "repo")]
    [InlineData("  https://github.com/user/My.Repo.git  ", "My.Repo")]
    public void RepositoryNameFromCloneUrl_HandlesCommonForms(string url, string expected)
    {
        Assert.Equal(expected, GitService.RepositoryNameFromCloneUrl(url));
    }
}
