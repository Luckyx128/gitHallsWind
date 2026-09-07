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
/// Groups a flat JQL result by status.
///
/// The order is the order Jira returned — a project's own workflow order, which
/// no sorting here could guess — with the single exception that "done" columns
/// sink to the bottom. Statuses are project-specific strings; the category is
/// the only field that means the same thing everywhere.
/// </summary>
public static class JiraIssueGrouping
{
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

        // OrderBy is stable, so everything that isn't "done" keeps the order it
        // arrived in.
        return order
            .OrderBy(status => string.Equals(categoryOf[status], "done", StringComparison.Ordinal))
            .Select(status => new JiraIssueGroup(status, categoryOf[status], byStatus[status]))
            .ToList();
    }
}
