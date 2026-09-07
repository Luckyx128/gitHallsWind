namespace GitHalls.Core.Models;

public enum FileChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Unmerged,
    Unknown
}

/// <summary>
/// The letter and colour a status gets in a file list. Shared, so a file in the
/// working tree and the same file in a commit are badged identically.
/// </summary>
public static class FileStatusBadge
{
    public static string Letter(FileChangeStatus status) => status switch
    {
        FileChangeStatus.Untracked => "U",
        FileChangeStatus.Modified => "M",
        FileChangeStatus.Added => "A",
        FileChangeStatus.Deleted => "D",
        FileChangeStatus.Renamed => "R",
        FileChangeStatus.Copied => "C",
        FileChangeStatus.Unmerged => "!",
        _ => "?"
    };

    public static string ColorHex(FileChangeStatus status) => status switch
    {
        FileChangeStatus.Untracked => "#28A745", // Green
        FileChangeStatus.Modified => "#0366D6", // Blue
        FileChangeStatus.Added => "#28A745", // Green
        FileChangeStatus.Deleted => "#D73A49", // Red
        FileChangeStatus.Renamed => "#6F42C1", // Purple
        FileChangeStatus.Unmerged => "#B31D28", // Dark Red
        _ => "#6A737D" // Gray
    };
}

public class FileChange
{
    public string Path { get; }
    public string? OriginalPath { get; } // For renames
    public FileChangeStatus IndexStatus { get; }
    public FileChangeStatus WorkTreeStatus { get; }

    public FileChange(string path, FileChangeStatus indexStatus, FileChangeStatus workTreeStatus, string? originalPath = null)
    {
        Path = path;
        IndexStatus = indexStatus;
        WorkTreeStatus = workTreeStatus;
        OriginalPath = originalPath;
    }

    public bool IsStaged => IndexStatus != FileChangeStatus.Unknown && IndexStatus != FileChangeStatus.Untracked;
    public bool IsUnstaged => WorkTreeStatus != FileChangeStatus.Unknown || IndexStatus == FileChangeStatus.Untracked;

    public string FileName => System.IO.Path.GetFileName(Path);
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? string.Empty;

    /// <summary>The status the badge speaks for: what the worktree says, if it says anything.</summary>
    private FileChangeStatus BadgeStatus =>
        IsUnstaged && WorkTreeStatus != FileChangeStatus.Unknown ? WorkTreeStatus : IndexStatus;

    public string BadgeLetter => FileStatusBadge.Letter(BadgeStatus);

    public string BadgeColorHex => FileStatusBadge.ColorHex(BadgeStatus);
}
