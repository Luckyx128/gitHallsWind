using GitHalls.Core.Commits;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Commits;

public class ConventionalCommitSuggesterTests
{
    private readonly ConventionalCommitSuggester _suggester = new();

    [Fact]
    public void SuggestType_NoChanges_ReturnsChore()
    {
        var result = _suggester.SuggestType(Array.Empty<FileChange>());
        Assert.Equal("chore", result);
    }

    [Fact]
    public void SuggestType_GitHubWorkflow_ReturnsCi()
    {
        var changes = new[] { new FileChange(".github/workflows/build.yml", FileChangeStatus.Modified, FileChangeStatus.Unknown) };
        var result = _suggester.SuggestType(changes);
        Assert.Equal("ci", result);
    }

    [Fact]
    public void SuggestType_MarkdownFile_ReturnsDocs()
    {
        var changes = new[] { new FileChange("README.md", FileChangeStatus.Modified, FileChangeStatus.Unknown) };
        var result = _suggester.SuggestType(changes);
        Assert.Equal("docs", result);
    }

    [Fact]
    public void SuggestType_CsprojFile_ReturnsBuild()
    {
        var changes = new[] { new FileChange("GitHalls.Core/GitHalls.Core.csproj", FileChangeStatus.Modified, FileChangeStatus.Unknown) };
        var result = _suggester.SuggestType(changes);
        Assert.Equal("build", result);
    }

    [Fact]
    public void SuggestType_TestFile_ReturnsTest()
    {
        var changes = new[] { new FileChange("GitHalls.Core.Tests/SomeTest.cs", FileChangeStatus.Modified, FileChangeStatus.Unknown) };
        var result = _suggester.SuggestType(changes);
        Assert.Equal("test", result);
    }
}
