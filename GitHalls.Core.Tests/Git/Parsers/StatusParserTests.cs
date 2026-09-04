using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git.Parsers;

public class StatusParserTests
{
    private readonly StatusParser _parser = new();

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        var result = _parser.Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NulTerminatedString_ParsesCorrectly()
    {
        // git status --porcelain=v1 -z
        // Format: XY path\0
        var input = "M  src/file1.cs\0A  src/file2.cs\0?? src/new_file.cs\0";
        var result = _parser.Parse(input);

        Assert.Equal(3, result.Count);
        
        Assert.Equal("src/file1.cs", result[0].Path);
        Assert.Equal(FileChangeStatus.Modified, result[0].IndexStatus);
        Assert.Equal(FileChangeStatus.Unknown, result[0].WorkTreeStatus);
        Assert.True(result[0].IsStaged);

        Assert.Equal("src/file2.cs", result[1].Path);
        Assert.Equal(FileChangeStatus.Added, result[1].IndexStatus);
        Assert.Equal(FileChangeStatus.Unknown, result[1].WorkTreeStatus);

        Assert.Equal("src/new_file.cs", result[2].Path);
        Assert.Equal(FileChangeStatus.Untracked, result[2].IndexStatus);
        Assert.Equal(FileChangeStatus.Untracked, result[2].WorkTreeStatus);
        Assert.False(result[2].IsStaged);
    }

    [Fact]
    public void Parse_RenamedFile_ParsesOriginalPathCorrectly()
    {
        // For renames: R  new_path\0old_path\0
        var input = "R  src/new_name.cs\0src/old_name.cs\0";
        var result = _parser.Parse(input);

        Assert.Single(result);
        Assert.Equal("src/new_name.cs", result[0].Path);
        Assert.Equal("src/old_name.cs", result[0].OriginalPath);
        Assert.Equal(FileChangeStatus.Renamed, result[0].IndexStatus);
    }

    [Fact]
    public void Parse_AccentedPath_ParsesCorrectly()
    {
        // The `-z` option prevents git from quoting paths with accents.
        var input = "M  src/repositório.md\0";
        var result = _parser.Parse(input);

        Assert.Single(result);
        Assert.Equal("src/repositório.md", result[0].Path);
        Assert.Equal(FileChangeStatus.Modified, result[0].IndexStatus);
    }
}
