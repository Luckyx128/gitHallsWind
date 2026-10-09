namespace GitHalls.Core.Jira;

public static class JiraSprintChoice
{
    /// <summary>
    /// Where a new issue goes unless the user says otherwise. Jira's create
    /// call always files it in the backlog, so the active sprint is chosen up
    /// front; with parallel sprints, the one that started first.
    /// </summary>
    public static JiraSprint? DefaultSprint(IEnumerable<JiraSprint> sprints) =>
        sprints.Where(sprint => sprint.IsActive)
               .OrderBy(sprint => sprint.StartDate ?? DateTimeOffset.MaxValue)
               .FirstOrDefault();

    /// <summary>Active first, then by start date; each sprint once, though several boards can show it.</summary>
    public static IReadOnlyList<JiraSprint> Ordered(IEnumerable<JiraSprint> sprints) =>
        sprints.GroupBy(sprint => sprint.Id)
               .Select(group => group.First())
               .OrderByDescending(sprint => sprint.IsActive)
               .ThenBy(sprint => sprint.StartDate ?? DateTimeOffset.MaxValue)
               .ToList();
}
