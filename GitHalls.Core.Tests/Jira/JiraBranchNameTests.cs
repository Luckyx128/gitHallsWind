using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraBranchNameTests
{
    [Theory]
    [InlineData("SWEB-6832", "Fix the login form", "SWEB-6832-fix-the-login-form")]
    [InlineData("ABC-1", "  Spaces   everywhere  ", "ABC-1-spaces-everywhere")]
    [InlineData("ABC-2", "Slashes/and:colons", "ABC-2-slashes-and-colons")]
    [InlineData("ABC-3", "Acentuação é comum", "ABC-3-acentua-o-comum")]
    public void Suggest_TurnsTheSummaryIntoSomethingGitAccepts(string key, string summary, string expected)
    {
        Assert.Equal(expected, JiraBranchName.Suggest(key, summary));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData(null)]
    public void Suggest_FallsBackToTheKeyAloneWhenNothingSurvives(string? summary)
    {
        Assert.Equal("ABC-9", JiraBranchName.Suggest("ABC-9", summary));
    }

    [Fact]
    public void Suggest_TruncatesWithoutLeavingATrailingDash()
    {
        var suggestion = JiraBranchName.Suggest("ABC-1", new string('a', 39) + " tail");

        Assert.StartsWith("ABC-1-", suggestion);
        Assert.DoesNotContain("--", suggestion);
        Assert.False(suggestion.EndsWith('-'), "a branch name should not end in a dash");
        // Key, dash, and at most the 40-character slug.
        Assert.True(suggestion.Length <= "ABC-1".Length + 1 + 40);
    }
}
