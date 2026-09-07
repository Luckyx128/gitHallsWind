using GitHalls.Core.Models;

namespace GitHalls.Core.Diff;

/// <summary>
/// Turns a unified diff into the two columns of a side-by-side view: what the
/// file was on the left, what it became on the right.
///
/// The contract the whole view rests on is that row N of one column is row N of
/// the other. Where one side has more lines than the other in a block, the
/// shorter side is padded with empty rows, so the two views stay in step under
/// one shared scroll position without either of them knowing about the other.
///
/// Each side keeps only its own line numbers, so its gutter needs one number
/// column instead of the unified view's two.
/// </summary>
public static class SideBySideDiff
{
    public static (FileDiff Left, FileDiff Right) Split(FileDiff diff)
    {
        var left = new List<DiffLine>(diff.Lines.Count);
        var right = new List<DiffLine>(diff.Lines.Count);

        // A removal is shown opposite the addition that replaced it, so both are
        // held back until the block ends.
        var removed = new List<DiffLine>();
        var added = new List<DiffLine>();

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case DiffLineType.Deletion:
                    removed.Add(line);
                    break;

                case DiffLineType.Addition:
                    added.Add(line);
                    break;

                default:
                    // Context and hunk headers are the same on both sides.
                    FlushBlock(removed, added, left, right);
                    left.Add(OldSide(line));
                    right.Add(NewSide(line));
                    break;
            }
        }

        FlushBlock(removed, added, left, right);

        return (new FileDiff(diff.FilePath, left, diff.IsBinary),
                new FileDiff(diff.FilePath, right, diff.IsBinary));
    }

    /// <summary>True when there is anything to put in two columns at all.</summary>
    public static bool HasTwoSides(FileDiff diff) =>
        !diff.IsBinary && diff.Lines.Any(l => l.Type is DiffLineType.Addition or DiffLineType.Deletion);

    private static void FlushBlock(List<DiffLine> removed, List<DiffLine> added, List<DiffLine> left, List<DiffLine> right)
    {
        var rows = Math.Max(removed.Count, added.Count);
        for (int i = 0; i < rows; i++)
        {
            left.Add(i < removed.Count ? OldSide(removed[i]) : Filler());
            right.Add(i < added.Count ? NewSide(added[i]) : Filler());
        }

        removed.Clear();
        added.Clear();
    }

    private static DiffLine OldSide(DiffLine line) =>
        new(line.Content, line.Type, line.OldLineNumber, null);

    private static DiffLine NewSide(DiffLine line) =>
        new(line.Content, line.Type, null, line.NewLineNumber);

    /// <summary>An empty row: the other column changed here and this one did not.</summary>
    private static DiffLine Filler() => new(string.Empty, DiffLineType.Context, null, null);
}
