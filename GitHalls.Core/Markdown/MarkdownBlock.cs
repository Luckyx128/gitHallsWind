namespace GitHalls.Core.Markdown;

public enum MarkdownSpanStyle
{
    Plain,
    Bold,
    Italic,
    Code,
    Link
}

/// <summary>
/// A run of text inside a line, and how it should be drawn.
///
/// One style per span rather than a set: nested emphasis is rare in a README,
/// and "***both***" reading as bold is a better trade than a parser nobody can
/// follow.
/// </summary>
public sealed record MarkdownSpan(string Text, MarkdownSpanStyle Style = MarkdownSpanStyle.Plain, string? Url = null);

/// <summary>
/// One block of a document. Blocks are what a renderer lays out vertically;
/// spans are what it draws inside one.
/// </summary>
public abstract record MarkdownBlock
{
    public sealed record Heading(int Level, IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

    public sealed record Paragraph(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

    /// <summary><c>Ordered</c> decides the bullet: a dot, or the number the file gave.</summary>
    public sealed record ListItem(IReadOnlyList<MarkdownSpan> Spans, bool Ordered, string Marker) : MarkdownBlock;

    public sealed record Quote(IReadOnlyList<MarkdownSpan> Spans) : MarkdownBlock;

    /// <summary>Fenced code. Kept verbatim — nothing inside it is markup.</summary>
    public sealed record Code(string Text, string? Language) : MarkdownBlock;

    /// <summary>
    /// A pipe table, row by row, left as written — without the "|---|---|"
    /// separator, which is markup rather than a row.
    ///
    /// Not laid out as a grid: that is two renderers' worth of work for a block
    /// most READMEs use once. Kept together and drawn monospaced, the columns
    /// still line up — which is the part that carries the meaning, and far
    /// better than the wrapped paragraph this used to become.
    /// </summary>
    public sealed record Table(IReadOnlyList<string> Rows) : MarkdownBlock;

    public sealed record Rule : MarkdownBlock;
}
