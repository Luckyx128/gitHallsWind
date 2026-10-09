namespace GitHalls.Core.Jira;

public sealed record JiraIssueType(string Id, string Name, bool IsSubtask)
{
    public override string ToString() => Name;
}
