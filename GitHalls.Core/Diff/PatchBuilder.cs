using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Diff;

/// <summary>
/// Rebuilds a unified patch containing only the lines the user picked, so
/// <c>git apply --cached</c> can move just those into or out of the index.
///
/// The whole point is that the result must be a patch git accepts on the first
/// try: the counts have to be right, the bytes have to be the ones git printed,
/// and the end-of-file markers have to survive. Everything here exists for one
/// of those three.
/// </summary>
public static class PatchBuilder
{
    /// <summary>git's own wording. LC_ALL=C in the runner is what makes it predictable.</summary>
    private const string NoNewlineMarker = "\\ No newline at end of file";

    /// <summary>
    /// The patch for <paramref name="selectedLineIndices"/> (indices into
    /// <see cref="FileDiff.Lines"/>), or null when there is nothing to apply.
    /// </summary>
    public static string? Build(
        FileDiff diff,
        IReadOnlySet<int> selectedLineIndices,
        PatchDirection direction)
    {
        if (diff == null || !diff.CanBuildPatch) return null;
        if (selectedLineIndices == null || selectedLineIndices.Count == 0) return null;

        var body = new StringBuilder();

        // The old side is the index, so its line numbers are already right. The
        // new side is the *result* of this patch, which differs from the index
        // only by the hunks that end up in it — hence a running total rather
        // than the numbers git printed.
        var netChange = 0;
        var wroteAnything = false;

        foreach (var hunk in diff.Hunks)
        {
            if (!hunk.HasContent) continue;

            var lines = new StringBuilder();
            var oldCount = 0;
            var newCount = 0;
            var hasSelection = false;

            for (int i = hunk.FirstLineIndex; i <= hunk.LastLineIndex; i++)
            {
                var line = diff.Lines[i];
                if (line.RawLine == null) return null; // synthesized: nothing to apply
                var isSelected = selectedLineIndices.Contains(i);

                switch (line.Type)
                {
                    case DiffLineType.Context:
                        Emit(lines, line.RawLine, line.NoNewlineAtEof);
                        oldCount++;
                        newCount++;
                        break;

                    case DiffLineType.Addition:
                        if (isSelected)
                        {
                            Emit(lines, line.RawLine, line.NoNewlineAtEof);
                            newCount++;
                            hasSelection = true;
                        }
                        else if (direction == PatchDirection.Reverse)
                        {
                            // Unstaging: an addition left alone stays in the
                            // index, so the patch has to see it on both sides.
                            Emit(lines, AsContext(line.RawLine), line.NoNewlineAtEof);
                            oldCount++;
                            newCount++;
                        }
                        // Forward: an addition left alone simply isn't staged.
                        break;

                    case DiffLineType.Deletion:
                        if (isSelected)
                        {
                            Emit(lines, line.RawLine, line.NoNewlineAtEof);
                            oldCount++;
                            hasSelection = true;
                        }
                        else if (direction == PatchDirection.Forward)
                        {
                            // Staging: a deletion left alone must survive in the
                            // index, so it is carried through as context.
                            Emit(lines, AsContext(line.RawLine), line.NoNewlineAtEof);
                            oldCount++;
                            newCount++;
                        }
                        // Reverse: a deletion left alone stays deleted.
                        break;
                }
            }

            // A hunk nobody picked a line in contributes nothing — not even a
            // shift, since the region comes out of the patch unchanged.
            if (!hasSelection) continue;

            // A unified diff numbers an *empty* range by the line it sits
            // after, so git's own start is one short there. Work from the real
            // first line, then step back again only if this patch empties the
            // range in turn.
            var oldAnchor = hunk.OldCount == 0 ? hunk.OldStart + 1 : hunk.OldStart;
            var newAnchor = oldAnchor + netChange;

            var oldStart = oldCount == 0 ? Math.Max(0, oldAnchor - 1) : oldAnchor;
            var newStart = newCount == 0 ? Math.Max(0, newAnchor - 1) : newAnchor;

            body.Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
                .Append(" +").Append(newStart).Append(',').Append(newCount)
                .Append(" @@\n")
                .Append(lines);

            netChange += newCount - oldCount;
            wroteAnything = true;
        }

        if (!wroteAnything) return null;

        return diff.PreambleText + body;
    }

    /// <summary>
    /// Writes one patch line, and the end-of-file marker it carried. Dropping
    /// that marker makes git append a newline the file never had.
    /// </summary>
    private static void Emit(StringBuilder builder, string rawLine, bool noNewlineAtEof)
    {
        builder.Append(rawLine).Append('\n');
        if (noNewlineAtEof) builder.Append(NoNewlineMarker).Append('\n');
    }

    /// <summary>Swaps a '+' or '-' marker for the context space, bytes untouched.</summary>
    private static string AsContext(string rawLine) => ' ' + rawLine.Substring(1);

    /// <summary>
    /// The changed lines of <paramref name="hunk"/> — what a "stage this whole
    /// block" gesture selects. Context is left out: it is emitted either way, so
    /// counting it would only inflate what the action bar reports.
    /// </summary>
    public static HashSet<int> ChangedLinesOf(FileDiff diff, DiffHunk hunk)
    {
        var set = new HashSet<int>();
        if (!hunk.HasContent) return set;

        for (int i = hunk.FirstLineIndex; i <= hunk.LastLineIndex; i++)
        {
            if (IsSelectable(diff.Lines[i])) set.Add(i);
        }

        return set;
    }

    /// <summary>Every changed line in the file.</summary>
    public static HashSet<int> AllChangedLines(FileDiff diff)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < diff.Lines.Count; i++)
        {
            if (IsSelectable(diff.Lines[i])) set.Add(i);
        }

        return set;
    }

    /// <summary>
    /// Only an addition or a deletion can be staged. Selecting context would
    /// change nothing, so the view refuses it rather than pretending.
    /// </summary>
    public static bool IsSelectable(DiffLine line) =>
        line.Type is DiffLineType.Addition or DiffLineType.Deletion;
}
