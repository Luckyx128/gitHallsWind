using GitHalls.Core.Markdown;
using Xunit;

namespace GitHalls.Core.Tests.Markdown;

public class ReadmeFinderTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("readme.markdown")]
    [InlineData("Readme.txt")]
    [InlineData("README")]
    public void Pick_FindsTheUsualSpellings(string name)
    {
        Assert.Equal(name, ReadmeFinder.Pick(new[] { name }));
    }

    [Fact]
    public void Pick_PrefersTheMarkdownOneWhenThereAreSeveral()
    {
        Assert.Equal("README.md", ReadmeFinder.Pick(new[] { "README.txt", "README.md" }));
        Assert.Equal("README.rst", ReadmeFinder.Pick(new[] { "README", "README.rst" }));
    }

    [Fact]
    public void Pick_IgnoresFilesThatMerelyMentionIt()
    {
        Assert.Null(ReadmeFinder.Pick(new[] { "READMEME.md", "read-me.md", "docs-readme.md" }));
    }

    [Fact]
    public void Pick_FindsNothingWhenThereIsNothing()
    {
        Assert.Null(ReadmeFinder.Pick(Array.Empty<string>()));
        Assert.Null(ReadmeFinder.Pick(new[] { "LICENSE", "GitHalls.sln" }));
    }
}

public class MarkdownParserTests
{
    private static T Single<T>(string source) where T : MarkdownBlock =>
        Assert.IsType<T>(Assert.Single(MarkdownParser.Parse(source)));

    [Theory]
    [InlineData("# One", 1, "One")]
    [InlineData("### Three", 3, "Three")]
    public void Parse_ReadsHeadingsByTheirLevel(string source, int level, string text)
    {
        var heading = Single<MarkdownBlock.Heading>(source);

        Assert.Equal(level, heading.Level);
        Assert.Equal(text, Assert.Single(heading.Spans).Text);
    }

    [Fact]
    public void Parse_DoesNotTakeAHashtagForAHeading()
    {
        // No space after the hashes, so it is prose.
        var paragraph = Single<MarkdownBlock.Paragraph>("#hashtag");

        Assert.Equal("#hashtag", Assert.Single(paragraph.Spans).Text);
    }

    [Fact]
    public void Parse_JoinsWrappedLinesIntoOneParagraph()
    {
        var blocks = MarkdownParser.Parse("one\ntwo\n\nthree");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("one two", Assert.Single(((MarkdownBlock.Paragraph)blocks[0]).Spans).Text);
        Assert.Equal("three", Assert.Single(((MarkdownBlock.Paragraph)blocks[1]).Spans).Text);
    }

    [Fact]
    public void Parse_KeepsFencedCodeVerbatim()
    {
        var code = Single<MarkdownBlock.Code>("```csharp\nvar x = **not bold**;\n# not a heading\n```");

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = **not bold**;\n# not a heading", code.Text);
    }

    [Fact]
    public void Parse_ReadsAnUnlabelledFence()
    {
        var code = Single<MarkdownBlock.Code>("```\nplain\n```");

        Assert.Null(code.Language);
        Assert.Equal("plain", code.Text);
    }

    [Fact]
    public void Parse_ReadsBothKindsOfList()
    {
        var bullet = Single<MarkdownBlock.ListItem>("- one");
        Assert.False(bullet.Ordered);
        Assert.Equal("•", bullet.Marker);

        var numbered = Single<MarkdownBlock.ListItem>("2. two");
        Assert.True(numbered.Ordered);
        Assert.Equal("2.", numbered.Marker);
    }

    [Fact]
    public void Parse_ReadsQuotesAndRules()
    {
        Assert.Equal("quoted", Assert.Single(Single<MarkdownBlock.Quote>("> quoted").Spans).Text);
        Single<MarkdownBlock.Rule>("---");
        Single<MarkdownBlock.Rule>("***");
    }

    [Fact]
    public void Parse_KeepsATableTogetherInsteadOfWrappingItIntoAParagraph()
    {
        var table = Single<MarkdownBlock.Table>("| Project | What |\n|---|---|\n| Core | Logic |");

        // The separator line is dropped: it is markup, not a row.
        Assert.Equal(new[] { "| Project | What |", "| Core | Logic |" }, table.Rows);
    }

    [Fact]
    public void Parse_DoesNotTakeAPipeInProseForATable()
    {
        var paragraph = Single<MarkdownBlock.Paragraph>("run a | b to pipe");

        Assert.Equal("run a | b to pipe", Assert.Single(paragraph.Spans).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\n\n   \n")]
    public void Parse_SurvivesAnEmptyDocument(string? source)
    {
        Assert.Empty(MarkdownParser.Parse(source));
    }
}

public class MarkdownInlineTests
{
    [Fact]
    public void Spans_ReadEmphasisAndCode()
    {
        var spans = MarkdownInline.Spans("a **b** c");
        Assert.Equal(3, spans.Count);
        Assert.Equal(MarkdownSpanStyle.Bold, spans[1].Style);
        Assert.Equal("b", spans[1].Text);

        Assert.Equal(MarkdownSpanStyle.Italic, MarkdownInline.Spans("a *b*")[1].Style);
        Assert.Equal(MarkdownSpanStyle.Code, MarkdownInline.Spans("run `git pull`")[1].Style);
    }

    [Fact]
    public void Spans_TreatBackticksAsLiteralSoMarkupInsideThemSurvives()
    {
        var span = Assert.Single(MarkdownInline.Spans("`**not bold**`"));

        Assert.Equal("**not bold**", span.Text);
        Assert.Equal(MarkdownSpanStyle.Code, span.Style);
    }

    [Fact]
    public void Spans_ReadALink()
    {
        var spans = MarkdownInline.Spans("see [docs](https://example.com) now");

        Assert.Equal(3, spans.Count);
        Assert.Equal("docs", spans[1].Text);
        Assert.Equal(MarkdownSpanStyle.Link, spans[1].Style);
        Assert.Equal("https://example.com", spans[1].Url);
    }

    [Fact]
    public void Spans_KeepALinkWithNoRealTargetAsText()
    {
        var span = Assert.Single(MarkdownInline.Spans("[label](#anchor)"));

        Assert.Equal("label", span.Text);
        Assert.Equal(MarkdownSpanStyle.Plain, span.Style);
    }

    [Theory]
    [InlineData("2 * 3 = 6")]
    [InlineData("a `b")]
    public void Spans_LeaveAnUnclosedDelimiterAsProse(string line)
    {
        var span = Assert.Single(MarkdownInline.Spans(line));

        Assert.Equal(line, span.Text);
        Assert.Equal(MarkdownSpanStyle.Plain, span.Style);
    }

    [Fact]
    public void Spans_HandlePlainText()
    {
        Assert.Equal("nothing special", Assert.Single(MarkdownInline.Spans("nothing special")).Text);
        Assert.Empty(MarkdownInline.Spans(""));
    }
}
