namespace GitHalls.Core.Jira;

/// <summary>What starting work on an issue turns out to mean, once its workflow has been asked.</summary>
public enum JiraStartWork
{
    /// <summary>There is a move into an in-progress status, and it is the one to make.</summary>
    Move,

    /// <summary>The issue is already in progress. Not an error, and not something to move again.</summary>
    AlreadyInProgress,

    /// <summary>This workflow offers no move into an in-progress status from where the issue stands.</summary>
    NoCandidate
}

public readonly record struct JiraStartWorkChoice(JiraStartWork Outcome, JiraTransition? Transition);

/// <summary>
/// The decisions a board makes about a project's workflow, kept out of the
/// views because they are the part worth testing.
/// </summary>
public static class JiraWorkflow
{
    /// <summary>
    /// Words a workflow uses for the move that starts work. Only ever a
    /// tie-breaker: the status category has already narrowed the field, and a
    /// project that names nothing recognisably still gets an answer.
    /// </summary>
    private static readonly string[] StartingWords =
    {
        "in progress", "progress", "start", "doing", "development",
        "andamento", "iniciar", "desenvolvimento", "execucao", "fazendo"
    };

    /// <summary>
    /// Which move starts work. Status names belong to the project; the
    /// category does not, so that is what decides which moves qualify at all.
    /// </summary>
    public static JiraStartWorkChoice StartWork(IReadOnlyList<JiraTransition> transitions, string currentStatusCategory)
    {
        // Moving an issue that is already in progress is not what the button
        // promises, and doing it silently would be worse than doing nothing.
        if (string.Equals(currentStatusCategory, "indeterminate", StringComparison.Ordinal))
        {
            return new JiraStartWorkChoice(JiraStartWork.AlreadyInProgress, null);
        }

        var candidates = transitions.Where(t => t.LeadsToInProgress).ToList();
        if (candidates.Count == 0) return new JiraStartWorkChoice(JiraStartWork.NoCandidate, null);
        if (candidates.Count == 1) return new JiraStartWorkChoice(JiraStartWork.Move, candidates[0]);

        foreach (var candidate in candidates)
        {
            if (NamedLikeStartingWork(candidate)) return new JiraStartWorkChoice(JiraStartWork.Move, candidate);
        }

        // Jira lists transitions in workflow order, so the first is the one the
        // workflow itself puts first. No prompt: a button that stops to ask
        // which of three moves you meant is no longer a one-click button, and
        // the result is named afterwards and is one right-click from correction.
        return new JiraStartWorkChoice(JiraStartWork.Move, candidates[0]);
    }

    private static bool NamedLikeStartingWork(JiraTransition transition)
    {
        var name = JiraText.Fold(transition.Name);
        var status = JiraText.Fold(transition.ToStatus);

        return StartingWords.Any(word => name.Contains(word, StringComparison.Ordinal)
                                      || status.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// What a menu item says. The target status, because that is the column the
    /// person is looking at — unless two moves land on the same one, when the
    /// transition's own name is the only thing telling them apart.
    /// </summary>
    public static string Label(JiraTransition transition, IReadOnlyList<JiraTransition> all)
    {
        var ambiguous = all.Count(t => string.Equals(t.ToStatus, transition.ToStatus, StringComparison.Ordinal)) > 1;
        return ambiguous ? $"{transition.ToStatus} ({transition.Name})" : transition.ToStatus;
    }
}
