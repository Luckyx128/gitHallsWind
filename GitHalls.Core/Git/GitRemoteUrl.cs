namespace GitHalls.Core.Git;

/// <summary>
/// Reads a GitHub remote URL. git writes it in two forms — "https://github.com/
/// owner/repo.git" and "git@github.com:owner/repo.git" — and either may already
/// carry a username ("https://user@github.com/owner/repo.git").
/// </summary>
public static class GitRemoteUrl
{
    private const string Host = "github.com";

    public static (string Owner, string Repository)? OwnerAndRepository(string? remoteUrl)
    {
        var text = (remoteUrl ?? string.Empty).Trim();

        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        var host = text.IndexOf(Host, StringComparison.OrdinalIgnoreCase);
        if (host < 0) return null;

        // Whatever separates the host from the path: ':' for SSH, '/' for HTTPS.
        var path = text[(host + Host.Length)..].Trim(':', '/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 ? (parts[0], parts[1]) : null;
    }

    /// <summary>The https remote that authenticates as <paramref name="username"/>.</summary>
    public static string WithUsername(string owner, string repository, string username) =>
        $"https://{username}@{Host}/{owner}/{repository}.git";
}
