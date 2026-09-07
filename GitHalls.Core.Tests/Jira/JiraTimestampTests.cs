using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraTimestampTests
{
    [Fact]
    public void Parse_ReadsJirasOffsetWithoutAColon()
    {
        // The exact shape Jira Cloud sends, and the reason this class exists.
        var parsed = JiraTimestamp.Parse("2026-08-07T14:02:11.123-0300");

        Assert.Equal(new DateTimeOffset(2026, 8, 7, 14, 2, 11, 123, TimeSpan.FromHours(-3)), parsed);
    }

    [Theory]
    [InlineData("2026-08-07T14:02:11.123+05:30", 5.5)]
    [InlineData("2026-08-07T14:02:11.123+0530", 5.5)]
    public void Parse_AcceptsTheOffsetEitherWay(string value, double offsetHours)
    {
        Assert.Equal(TimeSpan.FromHours(offsetHours), JiraTimestamp.Parse(value).Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void Parse_IsDefaultWhenThereIsNothingToRead(string? value)
    {
        // A missing date sorts to the bottom rather than throwing mid-list.
        Assert.Equal(default, JiraTimestamp.Parse(value));
    }
}
