using GitHalls.Core.GitHub;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.GitHub;

public class PullRequestDraftTests
{
    private static Commit CommitWith(string message) =>
        new("0123456789abcdef", "Ana", "ana@example.com", DateTimeOffset.Now, message);

    [Fact]
    public void From_OneCommit_IsThatCommit()
    {
        var draft = PullRequestDraft.From(
            new[] { CommitWith("feat(app): add login\n\nUses the token the helper already stores.") },
            "feature-SWEB-12903");

        Assert.Equal("feat(app): add login", draft.Title);
        Assert.Equal("Uses the token the helper already stores.", draft.Body);
    }

    [Fact]
    public void From_OneCommitWithNoBody_LeavesTheDescriptionEmpty()
    {
        var draft = PullRequestDraft.From(new[] { CommitWith("fix: null crash") }, "fix-crash");

        Assert.Equal("fix: null crash", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void From_SeveralCommits_FallsBackToTheBranchName()
    {
        var draft = PullRequestDraft.From(
            new[] { CommitWith("feat: two"), CommitWith("feat: one\n\nBody nobody should see here.") },
            "feature-SWEB-12903");

        Assert.Equal("SWEB-12903", draft.Title);
        Assert.Equal(string.Empty, draft.Body);
    }

    [Fact]
    public void From_NoCommitsAhead_StillSuggestsSomething()
    {
        var draft = PullRequestDraft.From(Array.Empty<Commit>(), "feature/add-login");

        Assert.Equal("Add login", draft.Title);
    }

    [Theory]
    [InlineData("feature-SWEB-12903", "SWEB-12903")]
    [InlineData("feature/SWEB-12903-login-screen", "SWEB-12903: login screen")]
    [InlineData("SWEB-12903", "SWEB-12903")]
    [InlineData("feature/add-login", "Add login")]
    [InlineData("fix/login_crash", "Login crash")]
    [InlineData("origin-less-name", "Origin less name")]
    [InlineData("main", "Main")]
    // Two words that only look like a key: the project part is not upper case.
    [InlineData("add-2fa-support", "Add 2fa support")]
    [InlineData("  feature/  ", "Feature")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TitleFromBranch_ReadsTheNameAsASentenceButKeepsTheKey(string? branch, string expected)
    {
        Assert.Equal(expected, PullRequestDraft.TitleFromBranch(branch));
    }
}
