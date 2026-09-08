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
/// The letter a status gets in a file list. Shared, so a file in the working
/// tree and the same file in a commit are badged identically.
///
/// The colour used to live here too, as a hex string. It moved to the app:
/// a hex string has one value, and the badge needs a light one and a dark one.
/// The status itself is what Core knows; which brush paints it is not.
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
    public FileChangeStatus BadgeStatus =>
        IsUnstaged && WorkTreeStatus != FileChangeStatus.Unknown ? WorkTreeStatus : IndexStatus;

    public string BadgeLetter => FileStatusBadge.Letter(BadgeStatus);
}
