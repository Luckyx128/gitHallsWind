namespace GitHalls.Core.Jira;

/// <summary>What control a createmeta field gets. One place decides it, so the form and the payload agree.</summary>
public enum JiraFieldInput
{
    Text, Adf, Number, Date, DateTime, Select, MultiSelect, Labels, Duration, Team,

    /// <summary>A user picker; not offered by the create form yet.</summary>
    Unsupported
}
