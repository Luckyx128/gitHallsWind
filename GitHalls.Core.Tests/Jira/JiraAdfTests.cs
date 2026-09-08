using System.Text.Json;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraAdfTests
{
    private static string Text(string json) => JiraAdf.ToPlainText(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void Paragraphs_BecomeLines()
    {
        var text = Text("""
        {"type":"doc","version":1,"content":[
          {"type":"paragraph","content":[{"type":"text","text":"Hello "},{"type":"text","text":"world","marks":[{"type":"strong"}]}]},
          {"type":"paragraph","content":[{"type":"text","text":"Second"}]}
        ]}
        """);

        Assert.Equal("Hello world\nSecond", text);
    }

    [Fact]
    public void Lists_GetMarkersAndNestedListsIndent()
    {
        var text = Text("""
        {"type":"doc","content":[
          {"type":"orderedList","content":[
            {"type":"listItem","content":[
              {"type":"paragraph","content":[{"type":"text","text":"first"}]},
              {"type":"bulletList","content":[
                {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"inner"}]}]}
              ]}
            ]},
            {"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"second"}]}]}
          ]}
        ]}
        """);

        Assert.Equal("1. first\n  • inner\n2. second", text);
    }

    [Fact]
    public void HardBreaksMentionsAndLinksReadInline()
    {
        var text = Text("""
        {"type":"doc","content":[
          {"type":"paragraph","content":[
            {"type":"text","text":"Ask "},
            {"type":"mention","attrs":{"id":"1","text":"@Ana"}},
            {"type":"hardBreak"},
            {"type":"inlineCard","attrs":{"url":"https://acme.atlassian.net/browse/APP-1"}},
            {"type":"text","text":" "},
            {"type":"emoji","attrs":{"shortName":":+1:","text":"👍"}}
          ]}
        ]}
        """);

        Assert.Equal("Ask @Ana\nhttps://acme.atlassian.net/browse/APP-1 👍", text);
    }

    [Fact]
    public void QuotesAndCodeKeepTheirShape()
    {
        var text = Text("""
        {"type":"doc","content":[
          {"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"quoted"}]}]},
          {"type":"codeBlock","attrs":{"language":"bash"},"content":[{"type":"text","text":"git status\ngit log"}]},
          {"type":"rule"},
          {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Title"}]}
        ]}
        """);

        Assert.Equal("> quoted\ngit status\ngit log\n———\nTitle", text);
    }

    [Fact]
    public void Tables_ReadRowByRow()
    {
        var text = Text("""
        {"type":"doc","content":[
          {"type":"table","content":[
            {"type":"tableRow","content":[
              {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]},
              {"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]}
            ]},
            {"type":"tableRow","content":[
              {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]},
              {"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"2"}]}]}
            ]}
          ]}
        ]}
        """);

        Assert.Equal("A | B\n1 | 2", text);
    }

    [Fact]
    public void BlankRunsCollapseAndUnknownNodesStillRead()
    {
        var text = Text("""
        {"type":"doc","content":[
          {"type":"paragraph","content":[]},
          {"type":"paragraph","content":[]},
          {"type":"somethingNew","content":[{"type":"paragraph","content":[{"type":"text","text":"still here"}]}]},
          {"type":"paragraph","content":[]},
          {"type":"paragraph","content":[]},
          {"type":"paragraph","content":[{"type":"text","text":"end"}]}
        ]}
        """);

        Assert.Equal("still here\n\nend", text);
    }

    [Fact]
    public void NothingIn_NothingOut()
    {
        Assert.Equal(string.Empty, JiraAdf.ToPlainText(null));
        Assert.Equal(string.Empty, Text("\"just a string\""));
        Assert.Equal(string.Empty, Text("""{"type":"doc","content":[]}"""));
    }
}
