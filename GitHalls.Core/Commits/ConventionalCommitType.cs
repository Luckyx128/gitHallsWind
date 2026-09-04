namespace GitHalls.Core.Commits;

public class ConventionalCommitType
{
    public string Type { get; }
    public string Description { get; }

    public ConventionalCommitType(string type, string description)
    {
        Type = type;
        Description = description;
    }

    public static readonly IReadOnlyList<ConventionalCommitType> All = new[]
    {
        new ConventionalCommitType("feat", "A new feature"),
        new ConventionalCommitType("fix", "A bug fix"),
        new ConventionalCommitType("docs", "Documentation only changes"),
        new ConventionalCommitType("style", "Changes that do not affect the meaning of the code"),
        new ConventionalCommitType("refactor", "A code change that neither fixes a bug nor adds a feature"),
        new ConventionalCommitType("perf", "A code change that improves performance"),
        new ConventionalCommitType("test", "Adding missing tests or correcting existing tests"),
        new ConventionalCommitType("build", "Changes that affect the build system or external dependencies"),
        new ConventionalCommitType("ci", "Changes to our CI configuration files and scripts"),
        new ConventionalCommitType("chore", "Other changes that don't modify src or test files"),
        new ConventionalCommitType("revert", "Reverts a previous commit")
    };
}
