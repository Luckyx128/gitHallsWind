namespace GitHalls.Core.Diff;

/// <summary>
/// Which way the patch <see cref="PatchBuilder"/> produces will be applied to
/// the index. It does not change the shape of the patch — a patch is always
/// written forward — only which unselected lines survive as context.
/// </summary>
public enum PatchDirection
{
    /// <summary>
    /// Staging: built from the working-tree diff and applied as-is, so the
    /// index moves towards the working tree.
    /// </summary>
    Forward,

    /// <summary>
    /// Unstaging: built from the index diff and applied with <c>--reverse</c>,
    /// so the index moves back towards HEAD.
    /// </summary>
    Reverse
}
