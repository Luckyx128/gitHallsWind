namespace GitHalls.Core.Jira;

/// <summary>
/// The word a branch name starts with, read off the issue type: "feature",
/// "fix" or "chore".
///
/// Deliberately not <c>ConventionalCommitType</c>. That list spells the type
/// "feat" because the Conventional Commits spec requires it in a commit
/// subject — changing that string to suit a branch name would change every
/// message the app writes. The two vocabularies also differ in size and
/// purpose: eleven entries to help someone word a commit, three words to say
/// what a branch is for.
/// </summary>
public static class JiraBranchType
{
    public const string Feature = "feature";
    public const string Fix = "fix";
    public const string Chore = "chore";

    /// <summary>Keys are already folded — <see cref="JiraText.Fold"/> is what looks them up.</summary>
    private static readonly Dictionary<string, string> ByType = new(StringComparer.Ordinal)
    {
        ["bug"] = Fix,
        ["bugfix"] = Fix,
        ["defect"] = Fix,
        ["defeito"] = Fix,
        ["erro"] = Fix,
        ["falha"] = Fix,
        ["incident"] = Fix,
        ["incidente"] = Fix,
        ["problema"] = Fix,

        ["story"] = Feature,
        ["historia"] = Feature,
        ["estoria"] = Feature,
        ["epic"] = Feature,
        ["epico"] = Feature,
        ["new feature"] = Feature,
        ["feature"] = Feature,
        ["funcionalidade"] = Feature,
        ["nova funcionalidade"] = Feature,
        ["improvement"] = Feature,
        ["enhancement"] = Feature,
        ["melhoria"] = Feature,

        ["task"] = Chore,
        ["tarefa"] = Chore,
        ["sub-task"] = Chore,
        ["subtask"] = Chore,
        ["subtarefa"] = Chore,
        ["sub-tarefa"] = Chore,
        ["chore"] = Chore,
        ["manutencao"] = Chore,
    };

    public static string For(JiraIssue issue) => For(issue.Type);

    public static string For(string? jiraType)
    {
        var folded = JiraText.Fold(jiraType);
        if (folded.Length == 0) return Feature;

        if (ByType.TryGetValue(folded, out var exact)) return exact;

        // A project is free to invent "Bug de Produção" or "Story - Frontend",
        // so the words are tried one at a time. By word and not by substring:
        // "Debug tooling" contains "bug" and is not a bug.
        foreach (var word in folded.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (ByType.TryGetValue(word, out var byWord)) return byWord;
        }

        return Feature;
    }

    private static readonly char[] WordSeparators = { ' ', '-', '_', '/', '\\', '.', ',', ':', ';', '(', ')', '[', ']' };
}
