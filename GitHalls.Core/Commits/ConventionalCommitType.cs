namespace GitHalls.Core.Commits;

/// <summary>
/// One Conventional Commits type, with the description shown in the reference
/// popup. Port of ConventionalCommitType.swift.
/// </summary>
public sealed class ConventionalCommitType
{
    public string Name { get; }
    public string Description { get; }

    private ConventionalCommitType(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>What the ComboBox shows.</summary>
    public override string ToString() => Name;

    public static readonly ConventionalCommitType Feat = new("feat", "A new feature for the user");
    public static readonly ConventionalCommitType Fix = new("fix", "A bug fix");
    public static readonly ConventionalCommitType Docs = new("docs", "Documentation only changes");
    public static readonly ConventionalCommitType Style = new("style", "Changes that don't affect code meaning (formatting, whitespace)");
    public static readonly ConventionalCommitType Refactor = new("refactor", "A code change that neither fixes a bug nor adds a feature");
    public static readonly ConventionalCommitType Perf = new("perf", "A code change that improves performance");
    public static readonly ConventionalCommitType Test = new("test", "Adding or correcting tests");
    public static readonly ConventionalCommitType Build = new("build", "Changes to the build system or external dependencies");
    public static readonly ConventionalCommitType Ci = new("ci", "Changes to CI configuration files and scripts");
    public static readonly ConventionalCommitType Chore = new("chore", "Other changes that don't modify src or test files");
    public static readonly ConventionalCommitType Revert = new("revert", "Reverts a previous commit");

    public static readonly IReadOnlyList<ConventionalCommitType> All = new[]
    {
        Feat, Fix, Docs, Style, Refactor, Perf, Test, Build, Ci, Chore, Revert
    };

    public static ConventionalCommitType? FromName(string? name)
        => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
