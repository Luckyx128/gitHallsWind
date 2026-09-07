namespace GitHalls.Core.Jira;

/// <summary>
/// What it takes to talk to a Jira Cloud site: the site itself, the account's
/// email, and an API token. Jira Cloud authenticates with HTTP Basic over the
/// pair, not with a bearer token.
/// </summary>
public sealed class JiraCredentials
{
    public Uri Site { get; }
    public string Email { get; }
    public string Token { get; }

    public JiraCredentials(Uri site, string email, string token)
    {
        Site = site;
        Email = email;
        Token = token;
    }

    public string AuthorizationHeader =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Email}:{Token}"));

    /// <summary>
    /// The site as a user is likely to type it: with or without a scheme, with
    /// or without a trailing slash. Returns null when there is no host to talk
    /// to, which is the only case the settings screen can't act on.
    /// </summary>
    public static Uri? NormalizeSite(string? raw)
    {
        var text = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (text.Length == 0) return null;

        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri
            : null;
    }
}
