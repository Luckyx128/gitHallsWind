using System.Globalization;
using System.Text.RegularExpressions;

namespace GitHalls.Core.Jira;

/// <summary>
/// Jira stamps its dates as "2026-08-07T14:02:11.123-0300" — an offset with no
/// colon, which is not what ISO 8601 parsing expects and not something a custom
/// "zzz" format accepts either. The colon is put back before parsing, rather
/// than hand-rolling a date parser around it.
/// </summary>
public static partial class JiraTimestamp
{
    public static DateTimeOffset Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;

        var normalized = OffsetWithoutColon().Replace(value.Trim(), "$1:$2");

        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var parsed)
            ? parsed
            : default;
    }

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
