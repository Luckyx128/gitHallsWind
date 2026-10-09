namespace GitHalls.Core.Jira;

/// <summary>One allowed value of a field: an option, a priority, a team.</summary>
public sealed record JiraFieldOption(string Id, string Label)
{
    public override string ToString() => Label;
}
