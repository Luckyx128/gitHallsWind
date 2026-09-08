namespace GitHalls.Core.Jira;

/// <summary>
/// One Jira issue. The search fills in what a card shows; the detail fetch
/// adds the rest (description, people, dates), which a list of fifty issues
/// has no use for and would only make the search slower.
/// </summary>
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

    // MARK: - Detail fields

    public string? AssigneeName { get; init; }
    public string? ReporterName { get; init; }
    public DateTimeOffset Created { get; init; }

    /// <summary>The description as plain text, already converted from Jira's document format. Null until the detail is fetched.</summary>
    public string? Description { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();

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

    /// <summary>Case-insensitive match on the two things a person remembers: the key and the title.</summary>
    public bool Matches(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        var text = filter.Trim();
        return Key.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Summary.Contains(text, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Who the credentials belong to, as Jira reports it.</summary>
public sealed record JiraAccount(string AccountId, string DisplayName);
