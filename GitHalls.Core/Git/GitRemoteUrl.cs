namespace GitHalls.Core.Git;

using System;

/// <summary>
/// Reads a Git remote URL. git writes it in two forms — "https://host/
/// owner/repo.git" and "git@host:owner/repo.git" — and either may already
/// carry a username ("https://user@host/owner/repo.git").
/// </summary>
public static class GitRemoteUrl
{
    public static (string Host, string Path)? ParseRemote(string? remoteUrl)
    {
        var text = (remoteUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        string host;
        string path;

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
            host = uri.Host;
            path = uri.AbsolutePath.TrimStart('/');
        }
        else
        {
            // Scp-like syntax: git@host:path
            var colonIndex = text.IndexOf(':');
            if (colonIndex < 0) return null;
            
            var beforeColon = text.Substring(0, colonIndex);
            var atIndex = beforeColon.IndexOf('@');
            host = atIndex >= 0 ? beforeColon.Substring(atIndex + 1) : beforeColon;
            path = text.Substring(colonIndex + 1);
        }

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(path)) return null;

        return (host, path);
    }

    /// <summary>The https remote that authenticates as <paramref name="username"/>.</summary>
    public static string WithUsername(string host, string path, string username) =>
        $"https://{username}@{host}/{path}.git";
}
