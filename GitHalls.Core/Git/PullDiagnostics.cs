namespace GitHalls.Core.Git;

/// <summary>
/// Reading what git said about a pull, for the two cases the exit code does not
/// tell apart on its own.
/// </summary>
public static class PullDiagnostics
{
    /// <summary>
    /// git refused the pull: merging would have written over work in progress.
    /// Recoverable without losing anything — the changes can be set aside for
    /// the pull and put back after.
    /// </summary>
    public static bool IsBlockedByLocalChanges(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        return message.Contains("would be overwritten by merge", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Please commit your changes or stash them", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pull landed, and putting the local changes back conflicted.
    ///
    /// git exits 0 here and says this only on stderr, so a caller that reads the
    /// exit code alone reports plain success and leaves the user in a conflicted
    /// tree with a stash they were never told about.
    /// </summary>
    public static bool AutostashConflicted(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        return message.Contains("Applying autostash resulted in conflicts", StringComparison.OrdinalIgnoreCase);
    }
}
