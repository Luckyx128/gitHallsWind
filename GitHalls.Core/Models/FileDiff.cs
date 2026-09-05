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
    /// belongs to the gutter, not to the selectable text.
    /// </summary>
    public string Content { get; }
    public DiffLineType Type { get; }
    public int? OldLineNumber { get; }
    public int? NewLineNumber { get; }

    public DiffLine(string content, DiffLineType type, int? oldLineNumber, int? newLineNumber)
    {
        Content = content;
        Type = type;
        OldLineNumber = oldLineNumber;
        NewLineNumber = newLineNumber;
    }
}

public class FileDiff
{
    public string FilePath { get; }
    public bool IsBinary { get; }
    public IReadOnlyList<DiffLine> Lines { get; }

    public FileDiff(string filePath, IReadOnlyList<DiffLine> lines, bool isBinary = false)
    {
        FilePath = filePath;
        Lines = lines;
        IsBinary = isBinary;
    }

    public int Additions => Lines?.Count(l => l.Type == DiffLineType.Addition) ?? 0;
    public int Deletions => Lines?.Count(l => l.Type == DiffLineType.Deletion) ?? 0;
}
