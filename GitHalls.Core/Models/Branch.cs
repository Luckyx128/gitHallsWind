namespace GitHalls.Core.Models;

public class Branch
{
    public string Name { get; }
    public bool IsCurrent { get; }
    public bool IsRemote { get; }
    public string? RemoteName { get; }

    public Branch(string name, bool isCurrent, bool isRemote = false, string? remoteName = null)
    {
        Name = name;
        IsCurrent = isCurrent;
        IsRemote = isRemote;
        RemoteName = remoteName;
    }
}
