using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class PullDiagnosticsTests
{
    /// <summary>Verbatim from git 2.50 on a dirty tree.</summary>
    private const string Refusal = """
        error: Your local changes to the following files would be overwritten by merge:
        	GitHalls.App/MainWindow.xaml.cs
        Please commit your changes or stash them before you merge.
        Aborting
        """;

    /// <summary>Verbatim from `git pull --autostash`, which exits 0 while saying this.</summary>
    private const string AutostashConflict = """
        Applying autostash resulted in conflicts.
        Your changes are safe in the stash.
        You can run "git stash pop" or "git stash drop" at any time.
        """;

    [Fact]
    public void IsBlockedByLocalChanges_RecognisesThePullGitRefusedToStartOn()
    {
        Assert.True(PullDiagnostics.IsBlockedByLocalChanges(Refusal));
    }

    [Fact]
    public void AutostashConflicted_RecognisesAnAutostashThatCouldNotBePutBack()
    {
        Assert.True(PullDiagnostics.AutostashConflicted(AutostashConflict));
    }

    [Fact]
    public void Diagnostics_DoNotConfuseTheTwo()
    {
        Assert.False(PullDiagnostics.AutostashConflicted(Refusal));
        Assert.False(PullDiagnostics.IsBlockedByLocalChanges(AutostashConflict));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("fatal: Need to specify how to reconcile divergent branches.")]
    [InlineData("error: failed to push some refs")]
    [InlineData("Already up to date.")]
    public void Diagnostics_StayQuietAboutEveryOtherOutcome(string? message)
    {
        Assert.False(PullDiagnostics.IsBlockedByLocalChanges(message));
        Assert.False(PullDiagnostics.AutostashConflicted(message));
    }
}
