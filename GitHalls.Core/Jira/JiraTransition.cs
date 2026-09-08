namespace GitHalls.Core.Jira;

/// <summary>
/// One edge out of the issue's current status, as its project's workflow
/// allows it right now.
///
/// The target status and its category are carried along, not just the id: a
/// move that knows where it lands lets the board be corrected in place, with
/// no second request to discover what everyone already knew.
/// </summary>
public sealed record JiraTransition(string Id, string Name, string ToStatus, string ToStatusCategory)
{
    /// <summary>Jira may ask for more fields before it accepts this one — a required resolution, say.</summary>
    public bool HasScreen { get; init; }

    public bool LeadsToInProgress => string.Equals(ToStatusCategory, "indeterminate", StringComparison.Ordinal);
}
