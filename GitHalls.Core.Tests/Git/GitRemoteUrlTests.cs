using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class GitRemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/Luckyx128/gitHallsWind.git", "github.com", "Luckyx128/gitHallsWind")]
    [InlineData("https://github.com/Luckyx128/gitHallsWind", "github.com", "Luckyx128/gitHallsWind")]
    [InlineData("git@github.com:Luckyx128/gitHallsWind.git", "github.com", "Luckyx128/gitHallsWind")]
    [InlineData("ssh://git@github.com/Luckyx128/gitHallsWind.git", "github.com", "Luckyx128/gitHallsWind")]
    [InlineData("  https://user@github.com/Luckyx128/gitHallsWind.git  ", "github.com", "Luckyx128/gitHallsWind")]
    [InlineData("https://gitlab.com/owner/repo.git", "gitlab.com", "owner/repo")]
    [InlineData("git@custom.server.com:group/subgroup/repo.git", "custom.server.com", "group/subgroup/repo")]
    public void ParseRemote_ReadsBothFormsGitWrites(string url, string host, string path)
    {
        var parsed = GitRemoteUrl.ParseRemote(url);

        Assert.NotNull(parsed);
        Assert.Equal(host, parsed!.Value.Host);
        Assert.Equal(path, parsed.Value.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseRemote_IsNullWhenThereIsNothingToRewrite(string? url)
    {
        Assert.Null(GitRemoteUrl.ParseRemote(url));
    }

    [Fact]
    public void WithUsername_BuildsTheRemoteThatAuthenticatesAsThatAccount()
    {
        Assert.Equal(
            "https://Luckyx128@github.com/Luckyx128/gitHallsWind.git",
            GitRemoteUrl.WithUsername("github.com", "Luckyx128/gitHallsWind", "Luckyx128"));
    }
}
