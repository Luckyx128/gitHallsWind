using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class GraphLogParser
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
            if (lines.Length < 7) continue;

            var hash = lines[0].Trim();
            var parentsStr = lines[1].Trim();
            var refsStr = lines[2].Trim();
            var authorName = lines[3].Trim();
            var authorEmail = lines[4].Trim();
            var dateStr = lines[5].Trim();
            
            DateTimeOffset date = DateTimeOffset.MinValue;
            if (DateTimeOffset.TryParse(dateStr, out var parsedDate))
            {
                date = parsedDate;
            }

            var messageLines = lines.Skip(6).Select(l => l.TrimEnd('\r'));
            var message = string.Join("\n", messageLines).Trim();

            var parents = string.IsNullOrEmpty(parentsStr) 
                ? Array.Empty<string>() 
                : parentsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var refs = string.IsNullOrEmpty(refsStr)
                ? Array.Empty<string>()
                : refsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(r => r.Trim())
                         .ToArray();

            commits.Add(new Commit(hash, authorName, authorEmail, date, message, parents, refs));
        }

        return commits;
    }
}
