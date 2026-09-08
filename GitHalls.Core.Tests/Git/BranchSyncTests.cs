using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class BranchSyncTests
{
    [Theory]
    [InlineData(false, 0, 0, SyncAction.Publish)]
    [InlineData(false, 3, 0, SyncAction.Publish)]
    [InlineData(true, 0, 0, SyncAction.UpToDate)]
    [InlineData(true, 2, 0, SyncAction.Push)]
    [InlineData(true, 0, 2, SyncAction.Pull)]
    [InlineData(true, 1, 1, SyncAction.PullThenPush)]
    [InlineData(true, 5, 3, SyncAction.PullThenPush)]
    public void ActionFor_PicksTheActionGitWouldAccept(bool hasUpstream, int ahead, int behind, SyncAction expected)
    {
        Assert.Equal(expected, BranchSync.ActionFor(hasUpstream, ahead, behind));
    }
}
