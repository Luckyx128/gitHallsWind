namespace GitHalls.Core.Models;

/// <summary>
/// One "@@ -a,b +c,d @@" block, keeping the ranges <see cref="DiffLine"/> alone
/// cannot carry. The line indices point into <see cref="FileDiff.Lines"/>, which
/// is what ties a hunk to a selection made in the view.
/// </summary>
public class DiffHunk
{
    public int OldStart { get; }
    public int OldCount { get; }
    public int NewStart { get; }
    public int NewCount { get; }

    /// <summary>Index of the header row itself.</summary>
    public int HeaderIndex { get; }

    /// <summary>First content row of the hunk, or -1 when the hunk has none.</summary>
    public int FirstLineIndex { get; }

    /// <summary>Last content row of the hunk, or -1 when the hunk has none.</summary>
    public int LastLineIndex { get; }

    public DiffHunk(
        int oldStart, int oldCount, int newStart, int newCount,
        int headerIndex, int firstLineIndex, int lastLineIndex)
    {
        OldStart = oldStart;
        OldCount = oldCount;
        NewStart = newStart;
        NewCount = newCount;
        HeaderIndex = headerIndex;
        FirstLineIndex = firstLineIndex;
        LastLineIndex = lastLineIndex;
    }

    public bool HasContent => FirstLineIndex >= 0;

    /// <summary>True when <paramref name="lineIndex"/> is one of this hunk's content rows.</summary>
    public bool Contains(int lineIndex) =>
        HasContent && lineIndex >= FirstLineIndex && lineIndex <= LastLineIndex;
}
