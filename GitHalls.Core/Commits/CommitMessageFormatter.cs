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

    [GeneratedRegex(@"^[a-zA-Z\-]+:\s.*$")]
    private static partial Regex TrailerRegex();
}
