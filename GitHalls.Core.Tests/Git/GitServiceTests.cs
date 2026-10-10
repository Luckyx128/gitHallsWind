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

    [Fact]
    public async Task UndoLastCommitAsync_RunsResetSoft()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.UndoLastCommitAsync("C:\\dummy");

        Assert.Equal(new[] { "reset", "--soft", "HEAD~1" }, invokedArgs);
    }

    [Fact]
    public async Task AmendCommitAsync_RunsCommitAmend()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.AmendCommitAsync("C:\\dummy", "New Summary", "New Description");

        Assert.Equal(new[] { "commit", "--amend", "-m", "New Summary", "-m", "New Description" }, invokedArgs);
    }

    [Fact]
    public async Task AmendCommitAsync_WithoutDescription_RunsCommitAmendWithoutSecondMessage()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.AmendCommitAsync("C:\\dummy", "New Summary");

        Assert.Equal(new[] { "commit", "--amend", "-m", "New Summary" }, invokedArgs);
    }

    [Fact]
    public async Task RevertCommitAsync_RunsRevert()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.RevertCommitAsync("C:\\dummy", "abc1234");

        Assert.Equal(new[] { "revert", "--no-edit", "abc1234" }, invokedArgs);
    }

    [Fact]
    public async Task RenameBranchAsync_RunsBranchM()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.RenameBranchAsync("C:\\dummy", "old-name", "new-name");

        Assert.Equal(new[] { "branch", "-m", "old-name", "new-name" }, invokedArgs);
    }

    [Fact]
    public async Task DeleteRemoteBranchAsync_RunsPushDelete()
    {
        var invokedArgs = new List<string>();
        var fakeRunner = new FakeProcessRunner((dir, args) =>
        {
            invokedArgs.AddRange(args);
            return new GitProcessResult(0, "", "");
        });
        var service = new GitService(fakeRunner);

        await service.DeleteRemoteBranchAsync("C:\\dummy", "origin", "branch-to-delete");

        Assert.Equal(new[] { "push", "origin", "--delete", "branch-to-delete" }, invokedArgs);
    }
}
