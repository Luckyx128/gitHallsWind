namespace GitHalls.Core.GitHub;

using System;
using System.Text;
using GitHalls.Core.Git;

/// <summary>
/// The GitHub page that opens a pull request, prefilled — used when the `gh`
/// CLI is not installed. GitHub takes the title and body as query parameters,
/// so the browser route loses nothing but the confirmation step.
/// Port of GitHubService.pullRequestBrowserURL.
/// </summary>
public static class PullRequestUrl
{
    /// <summary>
    /// Owner and repository of a GitHub remote, in either form git writes it.
    /// Null for any other host: the path shape below is GitHub's, and guessing
    /// it for GitLab or Bitbucket would send the user to a 404.
    /// </summary>
    public static (string Owner, string Repo)? OwnerAndRepo(string? remoteUrl)
    {
        if (GitRemoteUrl.ParseRemote(remoteUrl) is not { } parts) return null;
        if (!parts.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;

        var segments = parts.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        return (segments[0], segments[1]);
    }

    /// <summary>
    /// Without an explicit base, "/pull/new/&lt;head&gt;" lets GitHub pick the
    /// repository's default branch — the same thing `gh pr create` does when
    /// --base is omitted.
    /// </summary>
    public static string? ForBrowser(string? remoteUrl, string head, string? baseBranch, string title, string body)
    {
        if (OwnerAndRepo(remoteUrl) is not { } repo) return null;
        if (string.IsNullOrWhiteSpace(head)) return null;

        var url = new StringBuilder("https://github.com/")
            .Append(repo.Owner).Append('/').Append(repo.Repo);

        // A branch name may contain "/", which is a path separator here and must
        // stay one — only the segments around it are escaped.
        if (string.IsNullOrWhiteSpace(baseBranch))
        {
            url.Append("/pull/new/").Append(EscapeBranch(head));
        }
        else
        {
            url.Append("/compare/").Append(EscapeBranch(baseBranch)).Append("...").Append(EscapeBranch(head));
        }

        url.Append("?quick_pull=1");
        if (!string.IsNullOrWhiteSpace(title)) url.Append("&title=").Append(Uri.EscapeDataString(title));
        if (!string.IsNullOrWhiteSpace(body)) url.Append("&body=").Append(Uri.EscapeDataString(body));

        return url.ToString();
    }

    private static string EscapeBranch(string branch) =>
        string.Join('/', branch.Trim().Split('/').Select(Uri.EscapeDataString));
}
