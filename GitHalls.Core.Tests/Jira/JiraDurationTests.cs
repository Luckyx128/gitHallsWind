using System;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraDurationTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(60, "1m")]
    [InlineData(3600, "1h")]
    [InlineData(8 * 3600, "1d")]
    [InlineData(5 * 8 * 3600, "1w")]
    [InlineData(5 * 8 * 3600 + 2 * 8 * 3600 + 3 * 3600 + 30 * 60, "1w 2d 3h 30m")]
    public void Format_FormatsToJiraStyle(int seconds, string expected)
    {
        Assert.Equal(expected, JiraDuration.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("1m", 60)]
    [InlineData("1h", 3600)]
    [InlineData("1d", 8 * 3600)]
    [InlineData("1w", 5 * 8 * 3600)]
    [InlineData("1w 2d 3h 30m", 5 * 8 * 3600 + 2 * 8 * 3600 + 3 * 3600 + 30 * 60)]
    public void Seconds_ParsesJiraStyle(string text, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, JiraDuration.Seconds(text));
    }
}
