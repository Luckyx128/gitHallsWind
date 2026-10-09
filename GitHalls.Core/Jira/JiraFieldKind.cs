namespace GitHalls.Core.Jira;

/// <summary>How a field's value is written, read off its createmeta schema.</summary>
public enum JiraFieldKind
{
    String, Number, Date, DateTime, User, Option, Priority, Labels, Components, Adf,

    /// <summary><c>timetracking</c>: written as <c>{"originalEstimate": "2h 30m"}</c>.</summary>
    TimeTracking,

    /// <summary>The Team field; written as the team id string.</summary>
    Team,

    /// <summary>An array of options, versions, groups: written as <c>[{"id": …}]</c>.</summary>
    MultiOption,

    /// <summary>An array of users: written as <c>[{"accountId": …}]</c>.</summary>
    UserList,

    Other
}
