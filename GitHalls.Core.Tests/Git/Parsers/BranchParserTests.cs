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
