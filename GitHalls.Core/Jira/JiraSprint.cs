namespace GitHalls.Core.Jira;

public sealed record JiraSprint(int Id, string Name, string State)
{
    public DateTimeOffset? StartDate { get; init; }

    public bool IsActive => string.Equals(State, "active", StringComparison.Ordinal);

    public override string ToString() => IsActive ? $"{Name} (active)" : Name;
}
