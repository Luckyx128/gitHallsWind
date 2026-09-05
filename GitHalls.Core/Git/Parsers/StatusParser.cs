using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class StatusParser
{
    public IReadOnlyList<FileChange> Parse(string gitOutput)
    {
        if (string.IsNullOrEmpty(gitOutput)) return Array.Empty<FileChange>();

        var changes = new List<FileChange>();
        var tokens = gitOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length < 3) continue;

            var indexStatusChar = token[0];
            var workTreeStatusChar = token[1];
            var path = token.Substring(3);

            var indexStatus = MapStatus(indexStatusChar);
            var workTreeStatus = MapStatus(workTreeStatusChar);

            string? originalPath = null;
            if (indexStatus == FileChangeStatus.Renamed || indexStatus == FileChangeStatus.Copied ||
                workTreeStatus == FileChangeStatus.Renamed || workTreeStatus == FileChangeStatus.Copied)
            {
                if (i + 1 < tokens.Length)
                {
                    originalPath = tokens[++i];

                }
            }

            changes.Add(new FileChange(path, indexStatus, workTreeStatus, originalPath));
        }

        return changes;
    }

    private FileChangeStatus MapStatus(char status)
    {
        return status switch
        {
            ' ' => FileChangeStatus.Unknown,
            'M' => FileChangeStatus.Modified,
            'A' => FileChangeStatus.Added,
            'D' => FileChangeStatus.Deleted,
            'R' => FileChangeStatus.Renamed,
            'C' => FileChangeStatus.Copied,
            '?' => FileChangeStatus.Untracked,
            'U' => FileChangeStatus.Unmerged,
            _ => FileChangeStatus.Unknown
        };
    }
}
