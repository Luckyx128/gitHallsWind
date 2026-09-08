namespace GitHalls.Core.Models;

/// <summary>What the single sync button can do, given how the branch sits against its upstream.</summary>
public enum SyncAction
{
    UpToDate,
    Publish,
    Push,
    Pull,
    PullThenPush,
}

public static class BranchSync
{
    /// <summary>
    /// Picks the action for an ahead/behind pair.
    ///
    /// Diverged — ahead and behind at the same time — is its own action: git
    /// refuses a non-fast-forward push, so a button that says "Push" there is a
    /// button that cannot work. The remote work has to come down first.
    /// </summary>
    public static SyncAction ActionFor(bool hasUpstream, int ahead, int behind)
    {
        if (!hasUpstream) return SyncAction.Publish;
        if (ahead > 0 && behind > 0) return SyncAction.PullThenPush;
        if (ahead > 0) return SyncAction.Push;
        if (behind > 0) return SyncAction.Pull;
        return SyncAction.UpToDate;
    }
}
