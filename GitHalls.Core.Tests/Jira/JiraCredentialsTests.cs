using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraCredentialsTests
{
    [Theory]
    [InlineData("acme.atlassian.net", "https://acme.atlassian.net/")]
    [InlineData("https://acme.atlassian.net", "https://acme.atlassian.net/")]
    [InlineData("  https://acme.atlassian.net/  ", "https://acme.atlassian.net/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void NormalizeSite_AcceptsWhatAPersonWouldType(string raw, string expected)
    {
        Assert.Equal(expected, JiraCredentials.NormalizeSite(raw)?.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(null)]
    public void NormalizeSite_IsNullWhenThereIsNoHostToTalkTo(string? raw)
    {
        Assert.Null(JiraCredentials.NormalizeSite(raw));
    }

    [Fact]
    public void AuthorizationHeader_IsTheBase64OfEmailAndToken()
    {
        var credentials = new JiraCredentials(new Uri("https://acme.atlassian.net"), "me@acme.com", "t0ken");

        Assert.Equal("bWVAYWNtZS5jb206dDBrZW4=", credentials.AuthorizationHeader);
    }
}
