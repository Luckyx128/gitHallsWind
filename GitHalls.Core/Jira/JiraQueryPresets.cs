namespace GitHalls.Core.Jira;

/// <summary>
/// The queries every board starts with. The sprint ones order by Rank — the
/// order the team put the cards in on their own board — and the rest by
/// recency, which is what a personal worklist wants.
/// </summary>
public static class JiraQueryPresets
{
    public const string ActiveSprintId = "preset:active-sprint";
    public const string MySprintWorkId = "preset:my-sprint-work";
    public const string AssignedToMeId = "preset:assigned-to-me";
    public const string ReportedByMeId = "preset:reported-by-me";
    public const string RecentlyUpdatedId = "preset:recently-updated";

    /// <summary>The board opens on the active sprint: the cards the team is working on this week.</summary>
    public const string DefaultId = ActiveSprintId;

    public static IReadOnlyList<JiraQuery> All { get; } = new[]
    {
        new JiraQuery(ActiveSprintId, "Active sprint",
            "sprint in openSprints() ORDER BY Rank ASC", isBuiltIn: true),
        new JiraQuery(MySprintWorkId, "My work in the sprint",
            "sprint in openSprints() AND assignee = currentUser() ORDER BY Rank ASC", isBuiltIn: true),
        new JiraQuery(AssignedToMeId, "Assigned to me",
            "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC", isBuiltIn: true),
        new JiraQuery(ReportedByMeId, "Reported by me",
            "reporter = currentUser() ORDER BY updated DESC", isBuiltIn: true),
        new JiraQuery(RecentlyUpdatedId, "Recently updated",
            "updated >= -7d ORDER BY updated DESC", isBuiltIn: true)
    };

    public static JiraQuery Default => All.First(q => q.Id == DefaultId);

    /// <summary>
    /// Presets first, in their fixed order, then the user's own. A saved query
    /// that reuses a preset id is ignored rather than allowed to shadow it.
    /// </summary>
    public static IReadOnlyList<JiraQuery> Combine(IEnumerable<JiraQuery> custom)
    {
        var presetIds = new HashSet<string>(All.Select(q => q.Id), StringComparer.Ordinal);
        var result = new List<JiraQuery>(All);
        result.AddRange(custom.Where(q => !presetIds.Contains(q.Id)));
        return result;
    }
}
