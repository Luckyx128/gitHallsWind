using System.Text;

namespace GitHalls.Core.Jira;

/// <summary>
/// The branch name an issue suggests: "SWEB-6832-fix-the-login-form".
///
/// A suggestion, not a rule — the user edits it before anything is created.
/// Anything that isn't a letter or a digit collapses to a single dash, so a
/// summary written in any language still yields a name git accepts.
/// </summary>
public static class JiraBranchName
{
    /// <summary>Longest slug kept after the issue key.</summary>
    private const int MaxSlugLength = 40;

    public static string Suggest(JiraIssue issue) => Suggest(issue.Key, issue.Summary);

    public static string Suggest(string key, string? summary)
    {
        var slug = Slug(summary);
        return slug.Length == 0 ? key : $"{key}-{slug}";
    }

    private static string Slug(string? summary)
    {
        var builder = new StringBuilder();

        foreach (var character in (summary ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length <= MaxSlugLength ? slug : slug[..MaxSlugLength].Trim('-');
    }
}
