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

    public bool IsChangeLine => Type == DiffLineType.Addition || Type == DiffLineType.Deletion;
    
    // For UI selection/staging
    public bool IsSelected { get; set; } = false;
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
