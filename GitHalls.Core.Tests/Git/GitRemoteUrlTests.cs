using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class GitRemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    [InlineData("https://github.com/Luckyx128/gitHallsWind", "Luckyx128", "gitHallsWind")]
    [InlineData("git@github.com:Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    [InlineData("ssh://git@github.com/Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    [InlineData("  https://user@github.com/Luckyx128/gitHallsWind.git  ", "Luckyx128", "gitHallsWind")]
    public void OwnerAndRepository_ReadsBothFormsGitWrites(string url, string owner, string repository)
    {
        var parsed = GitRemoteUrl.OwnerAndRepository(url);

        Assert.NotNull(parsed);
        Assert.Equal(owner, parsed!.Value.Owner);
        Assert.Equal(repository, parsed.Value.Repository);
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo.git")]   // another host entirely
    [InlineData("https://github.com/owner")]            // no repository
    [InlineData("")]
    [InlineData(null)]
    public void OwnerAndRepository_IsNullWhenThereIsNothingToRewrite(string? url)
    {
        Assert.Null(GitRemoteUrl.OwnerAndRepository(url));
    }

    [Fact]
    public void WithUsername_BuildsTheRemoteThatAuthenticatesAsThatAccount()
    {
        Assert.Equal(
            "https://Luckyx128@github.com/Luckyx128/gitHallsWind.git",
            GitRemoteUrl.WithUsername("Luckyx128", "gitHallsWind", "Luckyx128"));
    }
}
