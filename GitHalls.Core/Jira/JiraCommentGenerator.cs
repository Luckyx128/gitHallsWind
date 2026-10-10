using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Jira;

public static class JiraCommentGenerator
{
    public static string GeneratePostPushComment(IReadOnlyList<Commit> commits)
    {
        if (commits == null || commits.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine($"Pushed {commits.Count} commit{(commits.Count == 1 ? "" : "s")}:");
        builder.AppendLine();

        foreach (var commit in commits)
        {
            builder.AppendLine($"* {commit.ShortHash} - {commit.Summary}");
        }

        return builder.ToString().TrimEnd();
    }
}
