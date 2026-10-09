using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class GitServiceTests
{
    [Theory]
    [InlineData("https://github.com/user/repo.git", "repo")]
    [InlineData("https://github.com/user/repo", "repo")]
    [InlineData("https://github.com/user/repo/", "repo")]
    [InlineData("https://github.com/user/repo.git/", "repo")]
    [InlineData("git@github.com:user/repo.git", "repo")]
    [InlineData("ssh://git@host:2222/user/repo.git", "repo")]
    [InlineData("  https://github.com/user/My.Repo.git  ", "My.Repo")]
    public void RepositoryNameFromCloneUrl_HandlesCommonForms(string url, string expected)
    {
        Assert.Equal(expected, GitService.RepositoryNameFromCloneUrl(url));
    }

    [Fact]
    public void Commit_IsPendingPush_DefaultsFalseAndCanBeModified()
    {
        var commit = new GitHalls.Core.Models.Commit("abc123456789", "Author", "author@test.com", DateTimeOffset.Now, "feat: test");
        Assert.False(commit.IsPendingPush);

        commit.IsPendingPush = true;
        Assert.True(commit.IsPendingPush);
    }

    [Fact]
    public async Task GetUnpushedCommitHashesAsync_CollectsAndDeduplicatesHashes()
    {
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            var argList = args.ToList();
            if (argList.Contains("--branches"))
            {
                return new GitProcessResult(0, "aaa111\nbbb222\n", "");
            }
            if (argList.Contains("@{u}..HEAD"))
            {
                return new GitProcessResult(0, "bbb222\nccc333\n", "");
            }
            return new GitProcessResult(0, "", "");
        });

        var service = new GitService(fakeRunner);
        var unpushed = await service.GetUnpushedCommitHashesAsync("C:\\dummy");

        Assert.Contains("aaa111", unpushed);
        Assert.Contains("bbb222", unpushed);
        Assert.Contains("ccc333", unpushed);
        Assert.Equal(3, unpushed.Count);
    }

    private class FakeProcessRunner : IGitProcessRunner
    {
        private readonly Func<string, IEnumerable<string>, GitProcessResult> _handler;

        public FakeProcessRunner(Func<string, IEnumerable<string>, GitProcessResult> handler)
        {
            _handler = handler;
        }

        public Task<GitProcessResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, string? stdinData = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_handler(workingDirectory, arguments));
        }

        public Task<(byte[] Output, int ExitCode)> RunBytesAsync(string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((Array.Empty<byte>(), 0));
        }
    }
}
