using System.Globalization;
using System.Text;

namespace GitHalls.Core.Jira;

/// <summary>
/// Folding for text Jira lets a project name freely: issue types and status
/// names arrive in whatever language and casing the admin typed, so nothing
/// that has to recognise them can compare them as they came.
/// </summary>
internal static class JiraText
{
    /// <summary>
    /// Lower case, accents removed, inner whitespace collapsed to one space.
    /// "Histórias  de Usuário" and "historia de usuario" have to answer the
    /// same question, and no culture-aware comparison does that for free.
    /// </summary>
    internal static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            // The accent itself is a separate character after FormD; dropping
            // the marks is what turns "ó" into "o".
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
