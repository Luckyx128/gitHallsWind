namespace GitHalls.Core.Jira;

/// <summary>
/// Durations as Jira people write them: "1w 2d 3h 30m". A day is eight hours
/// and a week five days, as Jira's default time tracking counts them.
/// </summary>
public static class JiraDuration
{
    /// <summary>
    /// Seconds in the text; null when it is empty, has anything but
    /// number-and-unit pairs, or adds up to nothing.
    /// </summary>
    public static int? Seconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var total = 0;
        var number = string.Empty;

        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character)) continue;

            if (char.IsAsciiDigit(character))
            {
                number += character;
                continue;
            }

            int? unit = character switch
            {
                'w' => 5 * 8 * 3600,
                'd' => 8 * 3600,
                'h' => 3600,
                'm' => 60,
                _ => null
            };

            if (unit == null || !int.TryParse(number, out var value)) return null;
            total += value * unit.Value;
            number = string.Empty;
        }

        return number.Length == 0 && total > 0 ? total : null;
    }

    /// <summary>
    /// Formats a time duration into Jira style (e.g., "1w 2d 3h 30m").
    /// Assumes 8 hours/day and 5 days/week (40 hours/week).
    /// </summary>
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0) return "0m";

        var totalSeconds = (long)duration.TotalSeconds;

        var weeks = totalSeconds / (5 * 8 * 3600);
        totalSeconds %= (5 * 8 * 3600);

        var days = totalSeconds / (8 * 3600);
        totalSeconds %= (8 * 3600);

        var hours = totalSeconds / 3600;
        totalSeconds %= 3600;

        var minutes = totalSeconds / 60;

        var parts = new System.Collections.Generic.List<string>();
        if (weeks > 0) parts.Add($"{weeks}w");
        if (days > 0) parts.Add($"{days}d");
        if (hours > 0) parts.Add($"{hours}h");
        if (minutes > 0) parts.Add($"{minutes}m");

        return parts.Count > 0 ? string.Join(" ", parts) : "0m";
    }
}
