namespace GitHalls.Core.Models;

/// <summary>
/// Which pair of trees a <see cref="FileDiff"/> compares. It decides whether a
/// selection can be staged or unstaged, and which way a patch built from it has
/// to be applied.
/// </summary>
public enum DiffSide
{
    /// <summary>
    /// Working tree against the index (<c>git diff</c>) — the only diff whose
    /// "old" side is the index, so the only one a patch can be staged from.
    /// </summary>
    WorkingTree,

    /// <summary>Index against HEAD (<c>git diff --cached</c>) — what can be unstaged.</summary>
    Index,

    /// <summary>
    /// Working tree against HEAD, or a commit's own diff. Shows everything that
    /// changed but cannot be applied to the index, so it is read-only.
    /// </summary>
    Combined
}
