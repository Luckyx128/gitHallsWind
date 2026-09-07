namespace GitHalls.Core.Jira;

/// <summary>One Jira issue, flattened to what the sidebar and the detail pane show.</summary>
public sealed class JiraIssue
{
    public string Key { get; }
    public string Summary { get; }
    public string Status { get; }

    /// <summary>
    /// "new", "indeterminate" or "done" — Jira's own coarse grouping, which is
    /// stable across projects in a way status names are not.
    /// </summary>
    public string StatusCategory { get; }

    public string Type { get; }
    public string? Priority { get; }
    public DateTimeOffset Updated { get; }

    public JiraIssue(string key, string summary, string status, string statusCategory,
                     string type, string? priority, DateTimeOffset updated)
    {
        Key = key;
        Summary = summary;
        Status = status;
        StatusCategory = statusCategory;
        Type = type;
        Priority = priority;
        Updated = updated;
    }

    public bool IsDone => string.Equals(StatusCategory, "done", StringComparison.Ordinal);
}

/// <summary>Who the credentials belong to, as Jira reports it.</summary>
public sealed record JiraAccount(string AccountId, string DisplayName);
