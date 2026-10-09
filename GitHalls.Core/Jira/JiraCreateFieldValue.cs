using System.Text;
using System.Text.Json;

namespace GitHalls.Core.Jira;

/// <summary>
/// Turns what the form collected for a required field into the JSON Jira
/// wants for it, and says what is wrong with it first.
/// </summary>
public static class JiraCreateFieldValue
{
    /// <summary>Handled by the form's own boxes, never by a generic field row.</summary>
    public static readonly IReadOnlySet<string> StandardKeys =
        new HashSet<string>(StringComparer.Ordinal) { "project", "issuetype", "summary", "description" };

    public static JiraFieldInput InputFor(JiraCreateField field) => field.Kind switch
    {
        JiraFieldKind.Adf => JiraFieldInput.Adf,
        JiraFieldKind.Number => JiraFieldInput.Number,
        JiraFieldKind.Date => JiraFieldInput.Date,
        JiraFieldKind.DateTime => JiraFieldInput.DateTime,
        JiraFieldKind.Team => JiraFieldInput.Team,
        JiraFieldKind.TimeTracking => JiraFieldInput.Duration,
        JiraFieldKind.Labels => JiraFieldInput.Labels,
        JiraFieldKind.MultiOption or JiraFieldKind.Components => JiraFieldInput.MultiSelect,
        JiraFieldKind.Option or JiraFieldKind.Priority => JiraFieldInput.Select,
        JiraFieldKind.User or JiraFieldKind.UserList => JiraFieldInput.Unsupported,
        _ => field.Allowed.Count == 0 ? JiraFieldInput.Text : JiraFieldInput.Select
    };

    /// <summary>The fields beyond the form's own that Jira insists on.</summary>
    public static IReadOnlyList<JiraCreateField> DynamicFields(IEnumerable<JiraCreateField> fields) =>
        fields.Where(field => field.NeedsInput && !StandardKeys.Contains(field.Key)).ToList();

    /// <summary>
    /// The first thing wrong with a text-like value, or null. <paramref name="text"/>
    /// is the box's text, or the chosen option or team id for a select.
    /// </summary>
    public static string? Problem(JiraCreateField field, string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0) return field.NeedsInput ? "Required." : null;

        return InputFor(field) switch
        {
            JiraFieldInput.Duration when JiraDuration.Seconds(value) == null => "Use a format like 2h 30m.",
            JiraFieldInput.Number when !double.TryParse(value, System.Globalization.NumberStyles.Float,
                                                        System.Globalization.CultureInfo.InvariantCulture, out _) => "Enter a number.",
            JiraFieldInput.Date when !DateOnly.TryParseExact(value, "yyyy-MM-dd", out _) => "Use yyyy-MM-dd.",
            _ => null
        };
    }

    /// <summary>The value to send for a text-like field; null when there is nothing to send.</summary>
    public static JsonElement? FromText(JiraCreateField field, string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0) return null;

        return InputFor(field) switch
        {
            JiraFieldInput.Number => double.TryParse(value, System.Globalization.NumberStyles.Float,
                                                     System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? Write(w => w.WriteNumberValue(number))
                : null,
            // A priority or option is {"id": …}; createmeta ids work for both.
            JiraFieldInput.Select => Write(w => { w.WriteStartObject(); w.WriteString("id", value); w.WriteEndObject(); }),
            JiraFieldInput.Labels => FromLabels(value),
            JiraFieldInput.Duration => Write(w => { w.WriteStartObject(); w.WriteString("originalEstimate", value); w.WriteEndObject(); }),
            JiraFieldInput.Adf => JiraAdf.FromPlainText(value),
            JiraFieldInput.Unsupported => null,
            // Text, dates and the Team id are plain strings.
            _ => Write(w => w.WriteStringValue(value))
        };
    }

    /// <summary>The value for a multi-select: <c>[{"id": …}]</c>, null when nothing is chosen.</summary>
    public static JsonElement? FromIds(IEnumerable<string> ids)
    {
        var list = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (list.Count == 0) return null;

        return Write(w =>
        {
            w.WriteStartArray();
            foreach (var id in list)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    /// <summary>Labels are typed comma or space separated; Jira labels cannot hold a space.</summary>
    public static IReadOnlyList<string> Labels(string text) =>
        text.Split(new[] { ',', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static JsonElement? FromLabels(string text)
    {
        var labels = Labels(text);
        if (labels.Count == 0) return null;

        return Write(w =>
        {
            w.WriteStartArray();
            foreach (var label in labels) w.WriteStringValue(label);
            w.WriteEndArray();
        });
    }

    internal static JsonElement Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
