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

    [Fact]
    public void InjectCoAuthors_AddsTrailerToEmptyMessage()
    {
        var result = CommitMessageFormatter.InjectCoAuthors("", new[] { "Alice <alice@test.com>" });
        Assert.Equal("Co-authored-by: Alice <alice@test.com>", result);
    }

    [Fact]
    public void InjectCoAuthors_CreatesTrailerBlockWhenNoneExists()
    {
        var message = "feat: add new feature";
        var expected = "feat: add new feature\n\nCo-authored-by: Alice <alice@test.com>";
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, new[] { "Alice <alice@test.com>" }));
    }

    [Fact]
    public void InjectCoAuthors_InsertsAfterRefsWhenRefsExists()
    {
        var message = "feat: add new feature\n\nRefs: ABC-123\nSigned-off-by: Me <me@test.com>";
        var expected = "feat: add new feature\n\nRefs: ABC-123\nCo-authored-by: Alice <alice@test.com>\nSigned-off-by: Me <me@test.com>";
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, new[] { "Alice <alice@test.com>" }));
    }

    [Fact]
    public void InjectCoAuthors_AppendsMultipleAuthorsInOrder()
    {
        var message = "feat: add new feature\n\nRefs: ABC-123";
        var expected = "feat: add new feature\n\nRefs: ABC-123\nCo-authored-by: Alice <alice@test.com>\nCo-authored-by: Bob <bob@test.com>";
        var authors = new[] { "Alice <alice@test.com>", "Bob <bob@test.com>" };
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, authors));
    }

    [Fact]
    public void InjectCoAuthors_DoesNotDuplicateTrailers()
    {
        var message = "feat: add new feature\n\nRefs: ABC-123\nCo-authored-by: Alice <alice@test.com>";
        var authors = new[] { "Alice <alice@test.com>", "Bob <bob@test.com>" };
        var expected = "feat: add new feature\n\nRefs: ABC-123\nCo-authored-by: Alice <alice@test.com>\nCo-authored-by: Bob <bob@test.com>";
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, authors));
    }

    [Fact]
    public void InjectCoAuthors_HandlesWindowsLineEndings()
    {
        var message = "feat: add new feature\r\n\r\nRefs: ABC-123";
        var expected = "feat: add new feature\n\nRefs: ABC-123\nCo-authored-by: Alice <alice@test.com>";
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, new[] { "Alice <alice@test.com>" }));
    }

    [Fact]
    public void InjectCoAuthors_HandlesAlreadyPrefixedTrailer()
    {
        var message = "feat: add new feature";
        var expected = "feat: add new feature\n\nCo-authored-by: Alice <alice@test.com>";
        Assert.Equal(expected, CommitMessageFormatter.InjectCoAuthors(message, new[] { "Co-authored-by: Alice <alice@test.com>" }));
    }

    [Fact]
    public void Commit_ExtractsCoAuthors_AndFormatsAuthorsDisplay()
    {
        var msg = "feat: great feature\n\nSome body\n\nCo-authored-by: Alice Smith <alice@example.com>\nCo-authored-by: Bob <bob@example.com>";
        var commit = new GitHalls.Core.Models.Commit("abc1234", "Lucas", "lucas@test.com", DateTimeOffset.UtcNow, msg);

        Assert.Equal(2, commit.CoAuthors.Count);
        Assert.Equal("Alice Smith <alice@example.com>", commit.CoAuthors[0]);
        Assert.Equal("Bob <bob@example.com>", commit.CoAuthors[1]);
        Assert.Equal("Lucas (+Alice Smith, Bob)", commit.AuthorsDisplay);
    }
}
