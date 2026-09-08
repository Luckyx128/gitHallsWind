namespace GitHalls.Core.Markdown;

/// <summary>
/// Splits one line into styled runs.
///
/// Left to right, one delimiter at a time: code first, because nothing inside
/// backticks is markup, then links, then emphasis. A delimiter that is never
/// closed is text — a README full of '*' in prose should read as prose, not
/// disappear into an unterminated italic.
/// </summary>
public static class MarkdownInline
{
    public static IReadOnlyList<MarkdownSpan> Spans(string? line)
    {
        var spans = new List<MarkdownSpan>();
        if (string.IsNullOrEmpty(line)) return spans;

        var plain = new System.Text.StringBuilder();
        var index = 0;

        void FlushPlain()
        {
            if (plain.Length == 0) return;

            spans.Add(new MarkdownSpan(plain.ToString()));
            plain.Clear();
        }

        while (index < line.Length)
        {
            var rest = line.AsSpan(index);

            if (rest[0] == '`' && Closing(line, index + 1, "`") is { } codeEnd)
            {
                FlushPlain();
                spans.Add(new MarkdownSpan(line[(index + 1)..codeEnd], MarkdownSpanStyle.Code));
                index = codeEnd + 1;
                continue;
            }

            if (rest[0] == '[' && TakeLink(line, index) is { } link)
            {
                FlushPlain();
                spans.Add(link.Span);
                index = link.Next;
                continue;
            }

            if (rest.StartsWith("**") && Closing(line, index + 2, "**") is { } boldEnd)
            {
                FlushPlain();
                spans.Add(new MarkdownSpan(line[(index + 2)..boldEnd], MarkdownSpanStyle.Bold));
                index = boldEnd + 2;
                continue;
            }

            if (rest[0] is '*' or '_')
            {
                var delimiter = rest[0].ToString();
                if (Closing(line, index + 1, delimiter) is { } italicEnd && italicEnd > index + 1)
                {
                    FlushPlain();
                    spans.Add(new MarkdownSpan(line[(index + 1)..italicEnd], MarkdownSpanStyle.Italic));
                    index = italicEnd + 1;
                    continue;
                }
            }

            plain.Append(line[index]);
            index++;
        }

        FlushPlain();
        return spans;
    }

    /// <summary>
    /// Where the delimiter closes, or null when it never does — which makes the
    /// opener ordinary text.
    /// </summary>
    private static int? Closing(string line, int from, string delimiter)
    {
        if (from > line.Length) return null;

        var at = line.IndexOf(delimiter, from, StringComparison.Ordinal);
        return at < 0 ? null : at;
    }

    /// <summary>
    /// "[label](target)". A target that is not a URL keeps the label as plain
    /// text rather than producing a link that goes nowhere.
    /// </summary>
    private static (MarkdownSpan Span, int Next)? TakeLink(string line, int index)
    {
        var labelEnd = line.IndexOf("](", index, StringComparison.Ordinal);
        if (labelEnd < 0) return null;

        var targetEnd = line.IndexOf(')', labelEnd + 2);
        if (targetEnd < 0) return null;

        var label = line[(index + 1)..labelEnd];
        var target = line[(labelEnd + 2)..targetEnd].Trim();

        var isUrl = Uri.TryCreate(target, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme);

        return (isUrl
            ? new MarkdownSpan(label, MarkdownSpanStyle.Link, target)
            : new MarkdownSpan(label), targetEnd + 1);
    }
}
