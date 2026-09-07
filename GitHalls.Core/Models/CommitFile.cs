namespace GitHalls.Core.Models;

/// <summary>
/// One file a commit touched: what happened to it and how many lines moved,
/// but not the diff itself.
///
/// This is what makes selecting a commit cheap. The whole list comes from a
/// single git call, and the diff of a file is fetched only when the user opens
/// that file — loading every diff up front cost one git process per file and
/// rendered diffs nobody had asked to see.
/// </summary>
public class CommitFile
{
    public string Path { get; }

    /// <summary>Where a renamed or copied file came from; null otherwise.</summary>
    public string? OriginalPath { get; }

    public FileChangeStatus Status { get; }
    public int Additions { get; }
    public int Deletions { get; }
    public bool IsBinary { get; }

    public CommitFile(
        string path,
        FileChangeStatus status,
        int additions = 0,
        int deletions = 0,
        bool isBinary = false,
        string? originalPath = null)
    {
        Path = path;
        Status = status;
        Additions = additions;
        Deletions = deletions;
        IsBinary = isBinary;
        OriginalPath = originalPath;
    }

    public string FileName => System.IO.Path.GetFileName(Path);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? string.Empty;

    public string BadgeLetter => FileStatusBadge.Letter(Status);
    public string BadgeColorHex => FileStatusBadge.ColorHex(Status);

    /// <summary>Line counts for the list row. Empty for a binary file, which has none.</summary>
    public string AdditionsText => IsBinary ? string.Empty : $"+{Additions}";
    public string DeletionsText => IsBinary ? string.Empty : $"-{Deletions}";
}
