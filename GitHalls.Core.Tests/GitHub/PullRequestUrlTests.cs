using GitHalls.Core.GitHub;
using Xunit;

namespace GitHalls.Core.Tests.GitHub;

public class PullRequestUrlTests
{
    [Theory]
    [InlineData("https://github.com/Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    [InlineData("git@github.com:Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    [InlineData("ssh://git@github.com/Luckyx128/gitHallsWind", "Luckyx128", "gitHallsWind")]
    [InlineData("https://Luckyx128@github.com/Luckyx128/gitHallsWind.git", "Luckyx128", "gitHallsWind")]
    public void OwnerAndRepo_ReadsBothFormsGitWrites(string url, string owner, string repo)
    {
        var parsed = PullRequestUrl.OwnerAndRepo(url);

        Assert.NotNull(parsed);
        Assert.Equal(owner, parsed!.Value.Owner);
        Assert.Equal(repo, parsed.Value.Repo);
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("git@bitbucket.org:owner/repo.git")]
    [InlineData("https://github.com/onlyowner")]
    [InlineData("")]
    [InlineData(null)]
    public void OwnerAndRepo_IsNullWhenItIsNotAGitHubRepository(string? url)
    {
        Assert.Null(PullRequestUrl.OwnerAndRepo(url));
    }

    [Fact]
    public void ForBrowser_WithoutBase_LetsGitHubPickTheDefaultBranch()
    {
        Assert.Equal(
            "https://github.com/Luckyx128/gitHallsWind/pull/new/feature/login?quick_pull=1&title=Add%20login",
            PullRequestUrl.ForBrowser(
                "git@github.com:Luckyx128/gitHallsWind.git",
                head: "feature/login", baseBranch: null, title: "Add login", body: ""));
    }

    [Fact]
    public void ForBrowser_WithBase_ComparesTheTwoBranches()
    {
        Assert.Equal(
            "https://github.com/Luckyx128/gitHallsWind/compare/main...feature/login?quick_pull=1&title=Add%20login&body=Closes%20%2312",
            PullRequestUrl.ForBrowser(
                "https://github.com/Luckyx128/gitHallsWind.git",
                head: "feature/login", baseBranch: "main", title: "Add login", body: "Closes #12"));
    }

    [Fact]
    public void ForBrowser_EscapesTheBranchNameWithoutEatingItsSlashes()
    {
        Assert.Equal(
            "https://github.com/o/r/pull/new/feat/caf%C3%A9%20bar?quick_pull=1",
            PullRequestUrl.ForBrowser("https://github.com/o/r", head: "feat/café bar", baseBranch: null, title: "", body: ""));
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo.git", "feature")]
    [InlineData("https://github.com/o/r", "")]
    public void ForBrowser_IsNullWhenThereIsNoPageToOpen(string url, string head)
    {
        Assert.Null(PullRequestUrl.ForBrowser(url, head, baseBranch: null, title: "t", body: ""));
    }
}
