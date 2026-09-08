using System.Text;
using System.Text.Json;

namespace GitHalls.Core.Jira;

/// <summary>
/// Flattens an Atlassian Document Format tree to readable text.
///
/// Jira Cloud v3 returns descriptions as ADF, a JSON tree of nodes, not as
/// text or markup. A detail window wants to read the description, not render
/// a document editor, so this keeps the structure a reader needs — paragraph
/// breaks, list bullets, quotes, code — and drops everything else.
/// </summary>
public static class JiraAdf
{
    public static string ToPlainText(JsonElement? document)
    {
        if (document is not { ValueKind: JsonValueKind.Object } root) return string.Empty;

        var builder = new StringBuilder();
        Append(root, builder, indent: string.Empty, listIndex: 0);
        return Tidy(builder.ToString());
    }

    private static void Append(JsonElement node, StringBuilder builder, string indent, int listIndex)
    {
        var type = node.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

        switch (type)
        {
            case "text":
                builder.Append(node.TryGetProperty("text", out var text) ? text.GetString() : string.Empty);
                break;

            case "hardBreak":
                builder.Append('\n').Append(indent);
                break;

            case "mention":
                builder.Append('@').Append(Attr(node, "text")?.TrimStart('@'));
                break;

            case "emoji":
                builder.Append(Attr(node, "text") ?? Attr(node, "shortName"));
                break;

            case "inlineCard":
            case "blockCard":
            case "embedCard":
                builder.Append(Attr(node, "url"));
                break;

            case "date":
                builder.Append(FormatDate(Attr(node, "timestamp")));
                break;

            case "rule":
                builder.Append(indent).Append("———\n");
                break;

            case "paragraph":
            case "heading":
            case "mediaGroup":
            case "mediaSingle":
                builder.Append(indent);
                AppendChildren(node, builder, indent, listIndex);
                builder.Append('\n');
                break;

            case "codeBlock":
                builder.Append(indent);
                AppendChildren(node, builder, indent, listIndex);
                builder.Append('\n');
                break;

            case "blockquote":
                AppendBlock(node, builder, indent + "> ", listIndex);
                break;

            case "panel":
            case "expand":
            case "nestedExpand":
                if (Attr(node, "title") is { Length: > 0 } title) builder.Append(indent).Append(title).Append('\n');
                AppendBlock(node, builder, indent, listIndex);
                break;

            case "bulletList":
                AppendList(node, builder, indent, ordered: false);
                break;

            case "orderedList":
                AppendList(node, builder, indent, ordered: true);
                break;

            case "listItem":
                // The marker was written by the list; the item's paragraphs follow
                // it on the same line, and any nested list goes under it.
                AppendListItem(node, builder, indent);
                break;

            case "table":
                AppendTable(node, builder, indent);
                break;

            case "media":
                builder.Append(Attr(node, "alt") ?? "[attachment]");
                break;

            default:
                // doc, tableRow, tableCell, tableHeader, taskList, decisionList,
                // and anything Atlassian adds later: the children still read.
                AppendChildren(node, builder, indent, listIndex);
                break;
        }
    }

    private static void AppendChildren(JsonElement node, StringBuilder builder, string indent, int listIndex)
    {
        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;

        foreach (var child in content.EnumerateArray()) Append(child, builder, indent, listIndex);
    }

    private static void AppendBlock(JsonElement node, StringBuilder builder, string indent, int listIndex)
    {
        AppendChildren(node, builder, indent, listIndex);
    }

    private static void AppendList(JsonElement node, StringBuilder builder, string indent, bool ordered)
    {
        if (!node.TryGetProperty("content", out var items) || items.ValueKind != JsonValueKind.Array) return;

        var number = 1;
        foreach (var item in items.EnumerateArray())
        {
            builder.Append(indent).Append(ordered ? $"{number}. " : "• ");
            number++;
            Append(item, builder, indent + "  ", number);
        }
    }

    private static void AppendListItem(JsonElement node, StringBuilder builder, string indent)
    {
        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;

        var first = true;
        foreach (var child in content.EnumerateArray())
        {
            var childType = child.TryGetProperty("type", out var t) ? t.GetString() : null;
            var isList = childType is "bulletList" or "orderedList";

            if (first && !isList)
            {
                // The first paragraph shares the marker's line, so it must not
                // re-indent itself.
                AppendChildren(child, builder, indent, 0);
                builder.Append('\n');
            }
            else
            {
                Append(child, builder, indent, 0);
            }

            first = false;
        }

        if (first) builder.Append('\n');
    }

    private static void AppendTable(JsonElement node, StringBuilder builder, string indent)
    {
        if (!node.TryGetProperty("content", out var rows) || rows.ValueKind != JsonValueKind.Array) return;

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("content", out var cells) || cells.ValueKind != JsonValueKind.Array) continue;

            var texts = new List<string>();
            foreach (var cell in cells.EnumerateArray())
            {
                var cellBuilder = new StringBuilder();
                AppendChildren(cell, cellBuilder, string.Empty, 0);
                texts.Add(cellBuilder.ToString().Trim().Replace('\n', ' '));
            }

            builder.Append(indent).Append(string.Join(" | ", texts)).Append('\n');
        }
    }

    private static string? Attr(JsonElement node, string name)
    {
        if (!node.TryGetProperty("attrs", out var attrs) || attrs.ValueKind != JsonValueKind.Object) return null;
        if (!attrs.TryGetProperty(name, out var value)) return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string FormatDate(string? timestamp)
    {
        if (long.TryParse(timestamp, out var millis))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis).ToString("yyyy-MM-dd");
        }

        return timestamp ?? string.Empty;
    }

    /// <summary>Trailing spaces off every line, never more than one blank line in a row, nothing at the ends.</summary>
    private static string Tidy(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder();
        var blankRun = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                blankRun++;
                if (blankRun > 1 || result.Length == 0) continue;
            }
            else
            {
                blankRun = 0;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
    }
}
