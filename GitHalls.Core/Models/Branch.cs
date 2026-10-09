namespace GitHalls.Core.Models;

public class Branch
{
    /// <summary>Display name — "main" for a local branch, "origin/main" for a remote one.</summary>
    public string Name { get; }
    public bool IsCurrent { get; }
    public bool IsRemote { get; }
    public string? RemoteName { get; }
    public bool HasUpstream { get; }
    public int Ahead { get; }
    public int Behind { get; }
    public bool IsLocalOnly => !IsRemote && !HasUpstream;
    public bool HasPendingPush => !IsRemote && (!HasUpstream || Ahead > 0);

    public Branch(
        string name,
        bool isCurrent,
        bool isRemote = false,
        string? remoteName = null,
        bool hasUpstream = false,
        int ahead = 0,
        int behind = 0)
    {
        Name = name;
        IsCurrent = isCurrent;
        IsRemote = isRemote;
        RemoteName = remoteName;
        HasUpstream = hasUpstream;
        Ahead = ahead;
        Behind = behind;
    }

    /// <summary>
    /// What to pass to <c>git checkout</c>. For a remote branch that is the name
    /// without its remote prefix: checking out "origin/main" literally detaches
    /// HEAD, whereas "main" creates a local branch tracking it.
    /// </summary>
    public string CheckoutName => IsRemote ? RemoteShortName(Name) : Name;

    /// <summary>Strips the remote prefix: "origin/feature/login" becomes "feature/login".</summary>
    public static string RemoteShortName(string remoteBranchName)
    {
        var separator = remoteBranchName.IndexOf('/');
        return separator < 0 ? remoteBranchName : remoteBranchName.Substring(separator + 1);
    }
}
