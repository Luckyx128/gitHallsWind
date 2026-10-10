using GitHalls.Core.Commits;
using Xunit;

namespace GitHalls.Core.Tests.Commits;

public class CommitMessageFormatterTests
{
    [Fact]
    public void InjectJiraRef_AddsTrailerToEmptyMessage()
    {
        Assert.Equal("Refs: ABC-123", CommitMessageFormatter.InjectJiraRef("", "ABC-123"));
    }

    [Fact]
    public void InjectJiraRef_CreatesTrailerBlockWhenNoneExists()
    {
        var message = "feat: add new feature";
        var expected = "feat: add new feature\n\nRefs: ABC-123";
        Assert.Equal(expected, CommitMessageFormatter.InjectJiraRef(message, "ABC-123"));
    }

    [Fact]
    public void InjectJiraRef_AppendsToExistingTrailerBlock()
    {
        var message = "feat: add new feature\n\nCo-authored-by: user";
        var expected = "feat: add new feature\n\nCo-authored-by: user\nRefs: ABC-123";
        Assert.Equal(expected, CommitMessageFormatter.InjectJiraRef(message, "ABC-123"));
    }

    [Fact]
    public void InjectJiraRef_DoesNotDuplicateTrailer()
    {
        var message = "feat: add new feature\n\nRefs: ABC-123";
        Assert.Equal(message, CommitMessageFormatter.InjectJiraRef(message, "ABC-123"));
    }

    [Fact]
    public void InjectJiraRef_HandlesWindowsLineEndings()
    {
        var message = "feat: add new feature\r\n\r\nCo-authored-by: user";
        var expected = "feat: add new feature\n\nCo-authored-by: user\nRefs: ABC-123";
        Assert.Equal(expected, CommitMessageFormatter.InjectJiraRef(message, "ABC-123"));
    }
}
