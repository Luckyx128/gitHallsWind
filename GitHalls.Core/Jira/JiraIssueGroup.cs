namespace GitHalls.Core.Jira;

/// <summary>Issues sharing a status, as one column of the board would hold them.</summary>
public sealed class JiraIssueGroup
{
    public string Status { get; }
    public string Category { get; }
    public IReadOnlyList<JiraIssue> Issues { get; }

    public JiraIssueGroup(string status, string category, IReadOnlyList<JiraIssue> issues)
    {
        Status = status;
        Category = category;
        Issues = issues;
    }

    public bool IsDone => string.Equals(Category, "done", StringComparison.Ordinal);
    public int Count => Issues.Count;

    /// <summary>Finished work is there to be looked up, not read — it starts closed.</summary>
    public bool StartsExpanded => !IsDone;
}

/// <summary>
/// Groups a flat JQL result by status, in board order.
///
/// Columns run left to right the way work flows: "new" statuses, then the ones
/// in progress, then "done". Within a category the order is the order Jira
/// returned — a project's own workflow order, which no sorting here could
/// guess. Statuses are project-specific strings; the category is the only
/// field that means the same thing everywhere.
/// </summary>
public static class JiraIssueGrouping
{
    /// <summary>Keeps every column, with only the issues that match the text. An empty column still holds its place on the board.</summary>
    public static IReadOnlyList<JiraIssueGroup> Filter(IReadOnlyList<JiraIssueGroup> groups, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return groups;

        return groups
            .Select(g => new JiraIssueGroup(g.Status, g.Category, g.Issues.Where(i => i.Matches(filter)).ToList()))
            .ToList();
    }

    private static int CategoryRank(string category) => category switch
    {
        "new" => 0,
        "done" => 2,
        _ => 1
    };

    public static IReadOnlyList<JiraIssueGroup> ByStatus(IEnumerable<JiraIssue> issues)
    {
        var order = new List<string>();
        var byStatus = new Dictionary<string, List<JiraIssue>>(StringComparer.Ordinal);
        var categoryOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var issue in issues)
        {
            if (!byStatus.TryGetValue(issue.Status, out var bucket))
            {
                bucket = new List<JiraIssue>();
                byStatus[issue.Status] = bucket;
                categoryOf[issue.Status] = issue.StatusCategory;
                order.Add(issue.Status);
            }

            bucket.Add(issue);
        }

        // OrderBy is stable, so within a category the columns keep the order
        // they arrived in.
        return order
            .OrderBy(status => CategoryRank(categoryOf[status]))
            .Select(status => new JiraIssueGroup(status, categoryOf[status], byStatus[status]))
            .ToList();
    }
}
