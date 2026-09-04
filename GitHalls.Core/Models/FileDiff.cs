namespace GitHalls.Core.Models;

public enum DiffLineType
{
    Context,
    Addition,
    Deletion,
    Header,
    Empty
}

public class DiffLine
{
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
}
