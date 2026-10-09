namespace GitHalls.Core.Jira;

public sealed record JiraProject(string Id, string Key, string Name)
{
    public override string ToString() => $"{Name} ({Key})";
}
