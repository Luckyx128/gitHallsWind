using System.Text;
using System.Text.RegularExpressions;

namespace GitHalls.Core.Commits;

public static partial class CommitMessageFormatter
{
    public static string InjectJiraRef(string message, string issueKey)
    {
        var trailer = $"Refs: {issueKey}";
        if (string.IsNullOrWhiteSpace(message))
            return trailer;

        message = message.Replace("\r\n", "\n").TrimEnd();
        if (message.Contains(trailer, StringComparison.OrdinalIgnoreCase))
            return message; // Already present

        var lines = message.Split('\n');
        
        // Find where the last block starts
        var lastEmptyLine = Array.LastIndexOf(lines, string.Empty);
        var lastBlockStart = lastEmptyLine + 1;

        var isTrailerBlock = lastBlockStart > 0;
        for (var i = lastBlockStart; i < lines.Length && isTrailerBlock; i++)
        {
            if (!TrailerRegex().IsMatch(lines[i]))
            {
                isTrailerBlock = false;
                break;
            }
        }

        var builder = new StringBuilder(message);
        
        if (isTrailerBlock && lastBlockStart < lines.Length)
        {
            builder.Append('\n').Append(trailer);
        }
        else
        {
            builder.Append("\n\n").Append(trailer);
        }

        return builder.ToString();
    }

    public static string InjectCoAuthors(string message, IEnumerable<string> coAuthors)
    {
        if (coAuthors == null)
            return message ?? string.Empty;

        var normalizedMessage = (message ?? string.Empty).Replace("\r\n", "\n").TrimEnd();

        var trailersToAdd = new List<string>();
        foreach (var coAuthor in coAuthors)
        {
            if (string.IsNullOrWhiteSpace(coAuthor))
                continue;

            var trailer = FormatCoAuthorTrailer(coAuthor);
            if (!normalizedMessage.Contains(trailer, StringComparison.OrdinalIgnoreCase)
                && !trailersToAdd.Contains(trailer, StringComparer.OrdinalIgnoreCase))
            {
                trailersToAdd.Add(trailer);
            }
        }

        if (trailersToAdd.Count == 0)
            return normalizedMessage;

        if (string.IsNullOrWhiteSpace(normalizedMessage))
            return string.Join("\n", trailersToAdd);

        var lines = normalizedMessage.Split('\n').ToList();
        var lastEmptyLine = lines.LastIndexOf(string.Empty);
        var lastBlockStart = lastEmptyLine + 1;

        var isTrailerBlock = lastBlockStart > 0;
        for (var i = lastBlockStart; i < lines.Count && isTrailerBlock; i++)
        {
            if (!TrailerRegex().IsMatch(lines[i]))
            {
                isTrailerBlock = false;
                break;
            }
        }

        if (isTrailerBlock)
        {
            var insertIndex = -1;
            for (var i = lines.Count - 1; i >= lastBlockStart; i--)
            {
                if (lines[i].StartsWith("Co-authored-by:", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].StartsWith("Refs:", StringComparison.OrdinalIgnoreCase))
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex >= 0)
            {
                lines.InsertRange(insertIndex + 1, trailersToAdd);
            }
            else
            {
                lines.AddRange(trailersToAdd);
            }

            return string.Join("\n", lines);
        }

        return normalizedMessage + "\n\n" + string.Join("\n", trailersToAdd);
    }

    private static string FormatCoAuthorTrailer(string author)
    {
        var trimmed = author.Trim();
        return trimmed.StartsWith("Co-authored-by:", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"Co-authored-by: {trimmed}";
    }

    [GeneratedRegex(@"^[a-zA-Z\-]+:\s.*$")]
    private static partial Regex TrailerRegex();
}
