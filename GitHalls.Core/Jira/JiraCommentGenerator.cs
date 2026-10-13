using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Jira;

public static class JiraCommentGenerator
{
    public static string GeneratePostPushComment(IReadOnlyList<Commit> commits, string branchName, string? repoName = null)
    {
        if (commits == null || commits.Count == 0)
            return string.Empty;

        var commitWord = commits.Count == 1 ? "commit enviado" : "commits enviados";
        var header = string.IsNullOrWhiteSpace(repoName)
            ? $"**{commits.Count} {commitWord} para {branchName}**"
            : $"**{commits.Count} {commitWord} para {branchName}** em {repoName}";

        var builder = new StringBuilder();
        builder.Append(header).Append("\n\n");

        foreach (var commit in commits)
        {
            builder.Append($"- [{commit.ShortHash}]({commit.Url}) {commit.Summary}\n");
        }

        builder.Append("\n_via GitHalls_");

        return builder.ToString();
    }

    public static string GeneratePostPushComment(IReadOnlyList<Commit> commits) =>
        GeneratePostPushComment(commits, "HEAD");
}
