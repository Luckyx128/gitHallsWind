using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git.Parsers;

public class BranchParserTests
{
    private readonly BranchParser _parser = new();

    [Fact]
    public void Parse_Empty_ReturnsEmptyList()
    {
        Assert.Empty(_parser.Parse(""));
    }

    [Fact]
    public void Parse_MarksCurrentBranch()
    {
        var result = _parser.Parse("* main\n  feature/login\n");

        Assert.Equal(2, result.Count);
        Assert.Equal("main", result[0].Name);
        Assert.True(result[0].IsCurrent);
        Assert.False(result[1].IsCurrent);
    }

    [Fact]
    public void Parse_SymbolicRemoteHead_IsSkipped()
    {
        // Real output of `git branch -a --no-color`. "remotes/origin/HEAD ->
        // origin/main" is a symbolic ref, not something you can check out.
        var output = "* main\n  remotes/origin/HEAD -> origin/main\n  remotes/origin/main\n";

        var result = _parser.Parse(output);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, b => b.Name.Contains("->"));
    }

    [Fact]
    public void Parse_RemoteBranch_KeepsRemotePrefixSoItDoesNotCollideWithLocal()
    {
        var result = _parser.Parse("* main\n  remotes/origin/main\n");

        Assert.Equal("main", result[0].Name);
        Assert.False(result[0].IsRemote);

        Assert.Equal("origin/main", result[1].Name);
        Assert.True(result[1].IsRemote);
        Assert.Equal("origin", result[1].RemoteName);
    }

    [Fact]
    public void Parse_RemoteBranchWithSlashInName_KeepsFullName()
    {
        var result = _parser.Parse("  remotes/upstream/feature/login\n");

        var branch = Assert.Single(result);
        Assert.Equal("upstream/feature/login", branch.Name);
        Assert.Equal("upstream", branch.RemoteName);
    }

    [Fact]
    public void Parse_DetachedHead_IsKept()
    {
        var result = _parser.Parse("* (HEAD detached at 1a2b3c4)\n  main\n");

        Assert.Equal("(HEAD detached at 1a2b3c4)", result[0].Name);
        Assert.True(result[0].IsCurrent);
    }

    [Fact]
    public void Parse_ForEachRef_ParsesLocalAndRemoteWithTracking()
    {
        var output = 
            "refs/heads/main|main|*|origin/main|ahead 2, behind 1\n" +
            "refs/heads/feature-local|feature-local| ||\n" +
            "refs/heads/feature-sync|feature-sync| |origin/feature-sync|\n" +
            "refs/remotes/origin/HEAD|origin| ||\n" +
            "refs/remotes/origin/main|origin/main| ||\n" +
            "refs/remotes/upstream/feature/login|upstream/feature/login| ||\n";

        var result = _parser.Parse(output);

        Assert.Equal(5, result.Count);

        // 1. main (current, local, tracking upstream, ahead 2, behind 1)
        var main = result[0];
        Assert.Equal("main", main.Name);
        Assert.True(main.IsCurrent);
        Assert.False(main.IsRemote);
        Assert.Null(main.RemoteName);
        Assert.True(main.HasUpstream);
        Assert.Equal(2, main.Ahead);
        Assert.Equal(1, main.Behind);
        Assert.False(main.IsLocalOnly);
        Assert.True(main.HasPendingPush);

        // 2. feature-local (local only, no upstream)
        var local = result[1];
        Assert.Equal("feature-local", local.Name);
        Assert.False(local.IsCurrent);
        Assert.False(local.IsRemote);
        Assert.False(local.HasUpstream);
        Assert.Equal(0, local.Ahead);
        Assert.Equal(0, local.Behind);
        Assert.True(local.IsLocalOnly);
        Assert.True(local.HasPendingPush);

        // 3. feature-sync (up to date with upstream)
        var sync = result[2];
        Assert.Equal("feature-sync", sync.Name);
        Assert.False(sync.IsCurrent);
        Assert.False(sync.IsRemote);
        Assert.True(sync.HasUpstream);
        Assert.Equal(0, sync.Ahead);
        Assert.Equal(0, sync.Behind);
        Assert.False(sync.IsLocalOnly);
        Assert.False(sync.HasPendingPush);

        // 4. origin/main (remote)
        var remoteMain = result[3];
        Assert.Equal("origin/main", remoteMain.Name);
        Assert.True(remoteMain.IsRemote);
        Assert.Equal("origin", remoteMain.RemoteName);
        Assert.False(remoteMain.IsLocalOnly);
        Assert.False(remoteMain.HasPendingPush);

        // 5. upstream/feature/login (remote with slash)
        var remoteFeature = result[4];
        Assert.Equal("upstream/feature/login", remoteFeature.Name);
        Assert.True(remoteFeature.IsRemote);
        Assert.Equal("upstream", remoteFeature.RemoteName);
        Assert.Equal("feature/login", remoteFeature.CheckoutName);
    }
}

public class BranchCheckoutNameTests
{
    [Fact]
    public void CheckoutName_Local_IsTheNameItself()
    {
        Assert.Equal("main", new Branch("main", isCurrent: true).CheckoutName);
    }

    [Fact]
    public void CheckoutName_Remote_DropsTheRemotePrefix()
    {
        // Checking out "origin/main" literally detaches HEAD; "main" creates a
        // local branch that tracks it.
        var branch = new Branch("origin/main", isCurrent: false, isRemote: true, remoteName: "origin");
        Assert.Equal("main", branch.CheckoutName);
    }

    [Fact]
    public void CheckoutName_RemoteWithSlashes_KeepsEverythingAfterTheRemote()
    {
        var branch = new Branch("upstream/feature/login", isCurrent: false, isRemote: true, remoteName: "upstream");
        Assert.Equal("feature/login", branch.CheckoutName);
    }
}

public class BranchModelTests
{
    [Theory]
    [InlineData(false, false, true)]  // Local, no upstream -> LocalOnly
    [InlineData(false, true, false)]  // Local, has upstream -> Not LocalOnly
    [InlineData(true, false, false)]  // Remote, no upstream -> Not LocalOnly
    [InlineData(true, true, false)]   // Remote, has upstream -> Not LocalOnly
    public void IsLocalOnly_ReturnsExpected(bool isRemote, bool hasUpstream, bool expected)
    {
        var branch = new Branch("test", isCurrent: false, isRemote: isRemote, hasUpstream: hasUpstream);
        Assert.Equal(expected, branch.IsLocalOnly);
    }

    [Theory]
    [InlineData(false, false, 0, true)]   // Local, no upstream, ahead 0 -> HasPendingPush
    [InlineData(false, true, 1, true)]    // Local, has upstream, ahead 1 -> HasPendingPush
    [InlineData(false, true, 0, false)]   // Local, has upstream, ahead 0 -> No pending push
    [InlineData(true, false, 5, false)]   // Remote -> Never pending push
    [InlineData(true, true, 5, false)]    // Remote -> Never pending push
    public void HasPendingPush_ReturnsExpected(bool isRemote, bool hasUpstream, int ahead, bool expected)
    {
        var branch = new Branch("test", isCurrent: false, isRemote: isRemote, hasUpstream: hasUpstream, ahead: ahead);
        Assert.Equal(expected, branch.HasPendingPush);
    }
}
