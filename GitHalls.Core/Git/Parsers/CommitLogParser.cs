using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class CommitLogParser
{
    private const string CommitDelimiter = "---COMMIT_END---";

    public IReadOnlyList<Commit> Parse(string logOutput)
    {
        if (string.IsNullOrWhiteSpace(logOutput)) return Array.Empty<Commit>();

        var commits = new List<Commit>();
        var commitBlocks = logOutput.Split(new[] { CommitDelimiter }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in commitBlocks)
        {
            var trimmedBlock = block.Trim();
            if (string.IsNullOrEmpty(trimmedBlock)) continue;

            var lines = trimmedBlock.Split('\n');
            if (lines.Length < 5) continue;

            var hash = lines[0].Trim();
            var authorName = lines[1].Trim();
            var authorEmail = lines[2].Trim();
            var dateStr = lines[3].Trim();
            
            DateTimeOffset date = DateTimeOffset.MinValue;
            if (DateTimeOffset.TryParse(dateStr, out var parsedDate))
            {
                date = parsedDate;
            }

            var messageLines = lines.Skip(4).Select(l => l.TrimEnd('\r')); // Re-join message
            var message = string.Join("\n", messageLines).Trim();

            commits.Add(new Commit(hash, authorName, authorEmail, date, message));
        }

        return commits;
    }
}
