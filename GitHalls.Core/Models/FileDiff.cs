namespace GitHalls.Core.Models;

public enum DiffLineType
{
    Context,
    Addition,
    Deletion,
    /// <summary>
    /// A friendly stand-in for git's raw "@@ -1,4 +1,5 @@" syntax, and the
    /// carrier for one-off notices ("Binary file not shown", "No content changes").
    /// </summary>
    HunkHeader
}

public class DiffLine
{
    /// <summary>
    /// Code content only — the leading '+', '-' or ' ' marker is stripped, it
    /// belongs to the gutter, not to the selectable text. A trailing CR is
    /// stripped too: it is part of the file's bytes, not of what is displayed.
    /// </summary>
    public string Content { get; }
    public DiffLineType Type { get; }
    public int? OldLineNumber { get; }
    public int? NewLineNumber { get; }

    /// <summary>
    /// Index into <see cref="FileDiff.Hunks"/>. A hunk header carries its own
    /// hunk's index — clicking it is how a whole block gets selected. Only a
    /// standalone notice ("Binary file not shown") has -1.
    /// </summary>
    public int HunkIndex { get; }

    /// <summary>
    /// The line exactly as git printed it — marker and CR included — or null
    /// when this line was synthesized rather than parsed.
    ///
    /// A patch is rebuilt from this, never from <see cref="Content"/>: dropping
    /// the CR of a CRLF file produces a patch that no longer matches the index,
    /// and git rejects it with "patch does not apply".
    /// </summary>
    public string? RawLine { get; }

    /// <summary>
    /// This line carried git's "\ No newline at end of file" marker. Losing it
    /// when rebuilding a patch makes git append a newline the file never had.
    /// </summary>
    public bool NoNewlineAtEof { get; }

    public DiffLine(
        string content,
        DiffLineType type,
        int? oldLineNumber,
        int? newLineNumber,
        int hunkIndex = -1,
        string? rawLine = null,
        bool noNewlineAtEof = false)
    {
        Content = content;
        Type = type;
        OldLineNumber = oldLineNumber;
        NewLineNumber = newLineNumber;
        HunkIndex = hunkIndex;
        RawLine = rawLine;
        NoNewlineAtEof = noNewlineAtEof;
    }

    /// <summary>
    /// The same line with the end-of-file marker set. The marker arrives on the
    /// line *after* the one it describes, so the parser has to go back for it.
    /// </summary>
    public DiffLine WithNoNewlineAtEof() =>
        new(Content, Type, OldLineNumber, NewLineNumber, HunkIndex, RawLine, noNewlineAtEof: true);
}

public class FileDiff
{
    public string FilePath { get; }
    public bool IsBinary { get; }
    public IReadOnlyList<DiffLine> Lines { get; }

    /// <summary>The hunks, in the order git printed them. Empty for a notice.</summary>
    public IReadOnlyList<DiffHunk> Hunks { get; }

    /// <summary>
    /// Everything git printed before the first "@@" — "diff --git", "index",
    /// the mode lines, "---" and "+++" — verbatim, newline-terminated. A patch
    /// rebuilt without it is not a patch git will read.
    /// </summary>
    public string PreambleText { get; }

    /// <summary>Which trees this diff compares, and so what can be done with it.</summary>
    public DiffSide Side { get; }

    public FileDiff(
        string filePath,
        IReadOnlyList<DiffLine> lines,
        bool isBinary = false,
        IReadOnlyList<DiffHunk>? hunks = null,
        string preambleText = "",
        DiffSide side = DiffSide.Combined)
    {
        FilePath = filePath;
        Lines = lines;
        IsBinary = isBinary;
        Hunks = hunks ?? Array.Empty<DiffHunk>();
        PreambleText = preambleText;
        Side = side;
    }

    public int Additions => Lines?.Count(l => l.Type == DiffLineType.Addition) ?? 0;
    public int Deletions => Lines?.Count(l => l.Type == DiffLineType.Deletion) ?? 0;

    /// <summary>
    /// Whether a patch can be built from this diff at all. A binary file, a
    /// notice ("No content changes") or a synthesized diff has nothing to apply.
    /// </summary>
    public bool CanBuildPatch =>
        !IsBinary && Hunks.Count > 0 && Side != DiffSide.Combined && PreambleText.Length > 0;

    /// <summary>The hunk owning <paramref name="lineIndex"/>, header row included, or null.</summary>
    public DiffHunk? HunkAt(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= Lines.Count) return null;

        var hunkIndex = Lines[lineIndex].HunkIndex;
        return hunkIndex >= 0 && hunkIndex < Hunks.Count ? Hunks[hunkIndex] : null;
    }
}
