using System.Text;

namespace GitHalls.Core.Jira;

/// <summary>
/// The branch name an issue suggests: "feature-SWEB-12903" — what the branch
/// is for, then the card it belongs to.
///
/// The summary is deliberately not part of it. A branch is looked up by its
/// card, and the key is what people say out loud; a slug of the title only
/// made the name longer and turned every rename of the card into a name that
/// no longer matched. A suggestion, not a rule — the user edits it before
/// anything is created.
/// </summary>
public static class JiraBranchName
{
    public static string Suggest(JiraIssue issue) => SuggestFor(issue.Key, issue.Type);

    /// <summary>
    /// Named apart from <see cref="Suggest(JiraIssue)"/> on purpose: the old
    /// two-argument overload took the summary, and a signature that keeps its
    /// shape while reversing its meaning is one a stale call site passes
    /// silently. This name makes such a call site fail to compile.
    /// </summary>
    public static string SuggestFor(string key, string? jiraType)
    {
        var type = JiraBranchType.For(jiraType);
        var trimmed = SanitizeKey(key);

        return trimmed.Length == 0 ? type : $"{type}-{trimmed}";
    }

    /// <summary>
    /// Keeps the key exactly as Jira spells it, upper case included, and
    /// collapses anything git would refuse into a single dash.
    /// </summary>
    private static string SanitizeKey(string? key)
    {
        var builder = new StringBuilder();

        foreach (var character in (key ?? string.Empty).Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
