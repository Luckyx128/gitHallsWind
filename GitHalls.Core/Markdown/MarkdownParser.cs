namespace GitHalls.Core.Markdown;

/// <summary>
/// Enough Markdown for a README: headings, paragraphs, lists, quotes, fenced
/// code, tables, rules, and inline emphasis. Deliberately not a full
/// implementation — footnotes and reference links render as the text they are
/// made of, which is worse than nothing only if you expected a browser.
/// </summary>
public static class MarkdownParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;

            blocks.Add(new MarkdownBlock.Paragraph(MarkdownInline.Spans(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // A fence swallows everything up to its closing pair, markup and
            // all — which is the whole point of a code block.
            if (Fence(trimmed) is { } language)
            {
                FlushParagraph();

                var body = new List<string>();
                while (++i < lines.Length && Fence(lines[i].Trim()) == null)
                {
                    body.Add(lines[i]);
                }

                blocks.Add(new MarkdownBlock.Code(string.Join("\n", body), language.Length == 0 ? null : language));
                continue;
            }

            // A pipe line is only a table when the next line is its separator;
            // otherwise it is prose that happens to contain a pipe.
            if (trimmed.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1].Trim()))
            {
                FlushParagraph();

                var rows = new List<string> { trimmed };
                i++;

                while (i + 1 < lines.Length && lines[i + 1].Contains('|'))
                {
                    rows.Add(lines[++i].Trim());
                }

                blocks.Add(new MarkdownBlock.Table(rows));
                continue;
            }

            if (IsRule(trimmed))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock.Rule());
                continue;
            }

            if (Heading(trimmed) is { } heading)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock.Heading(heading.Level, MarkdownInline.Spans(heading.Text)));
                continue;
            }

            if (ListItem(trimmed) is { } item)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock.ListItem(MarkdownInline.Spans(item.Text), item.Ordered, item.Marker));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock.Quote(MarkdownInline.Spans(trimmed[1..].Trim())));
                continue;
            }

            paragraph.Add(trimmed);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>The language after the fence, or null when the line is not a fence.</summary>
    private static string? Fence(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal)
            ? line[3..].Trim()
            : null;

    /// <summary>"|---|---|" — dashes, colons and pipes, nothing else.</summary>
    private static bool IsTableSeparator(string line) =>
        line.Contains('|') && line.Contains('-')
        && line.All(c => c is '|' or '-' or ':' or ' ');

    private static bool IsRule(string line)
    {
        var stripped = line.Replace(" ", string.Empty);
        if (stripped.Length < 3) return false;

        return stripped.All(c => c == '-') || stripped.All(c => c == '*') || stripped.All(c => c == '_');
    }

    private static (int Level, string Text)? Heading(string line)
    {
        var hashes = line.TakeWhile(c => c == '#').Count();
        if (hashes is < 1 or > 6) return null;

        var rest = line[hashes..];

        // "#hashtag" is not a heading; a heading has a space after its hashes.
        if (rest.Length > 0 && !rest.StartsWith(' ')) return null;

        return (hashes, rest.Trim());
    }

    private static (string Text, bool Ordered, string Marker)? ListItem(string line)
    {
        foreach (var bullet in new[] { "- ", "* ", "+ " })
        {
            if (line.StartsWith(bullet, StringComparison.Ordinal))
            {
                return (line[2..].Trim(), false, "•");
            }
        }

        // "1. ", "12) " — the file's own numbering is kept rather than recounted.
        var digits = new string(line.TakeWhile(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0 || digits.Length > 9) return null;

        var afterDigits = line[digits.Length..];
        if (!afterDigits.StartsWith(". ", StringComparison.Ordinal) && !afterDigits.StartsWith(") ", StringComparison.Ordinal))
        {
            return null;
        }

        return (afterDigits[2..].Trim(), true, $"{digits}.");
    }
}
