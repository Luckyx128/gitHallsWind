using GitHalls.Core.Commits;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Commits;

public class ConventionalCommitSuggesterTests
{
    private static FileChange Staged(string path, FileChangeStatus status = FileChangeStatus.Modified)
        => new(path, status, FileChangeStatus.Unknown);

    [Fact]
    public void SuggestType_NoChanges_ReturnsChore()
    {
        Assert.Same(ConventionalCommitType.Chore, ConventionalCommitSuggester.SuggestType(Array.Empty<FileChange>()));
    }

    [Theory]
    [InlineData("GitHalls.Core.Tests/Git/Parsers/DiffParserTests.cs")]
    [InlineData("tests/helpers.py")]
    [InlineData("src/app.spec.ts")]
    public void SuggestType_OnlyTests_ReturnsTest(string path)
    {
        Assert.Same(ConventionalCommitType.Test, ConventionalCommitSuggester.SuggestType(new[] { Staged(path) }));
    }

    [Fact]
    public void SuggestType_TestPlusSource_IsNotTest()
    {
        // Every rule is "all files agree" — a commit that also changes source is
        // not a test commit.
        var changes = new[] { Staged("GitHalls.Core.Tests/SomeTests.cs"), Staged("GitHalls.Core/Git/GitService.cs") };
        Assert.NotSame(ConventionalCommitType.Test, ConventionalCommitSuggester.SuggestType(changes));
    }

    [Fact]
    public void SuggestType_OnlyMarkdown_ReturnsDocs()
    {
        Assert.Same(ConventionalCommitType.Docs, ConventionalCommitSuggester.SuggestType(new[] { Staged("README.md") }));
    }

    [Fact]
    public void SuggestType_Workflow_ReturnsCi()
    {
        Assert.Same(ConventionalCommitType.Ci, ConventionalCommitSuggester.SuggestType(new[] { Staged(".github/workflows/build.yml") }));
    }

    [Fact]
    public void SuggestType_ProjectFile_ReturnsBuild()
    {
        Assert.Same(ConventionalCommitType.Build, ConventionalCommitSuggester.SuggestType(new[] { Staged("GitHalls.Core/GitHalls.Core.csproj") }));
    }

    [Fact]
    public void SuggestType_AllAdded_ReturnsFeat()
    {
        var changes = new[] { Staged("src/Feature.cs", FileChangeStatus.Added), Staged("src/Other.cs", FileChangeStatus.Added) };
        Assert.Same(ConventionalCommitType.Feat, ConventionalCommitSuggester.SuggestType(changes));
    }

    [Fact]
    public void SuggestType_AllDeleted_ReturnsChore()
    {
        Assert.Same(ConventionalCommitType.Chore,
            ConventionalCommitSuggester.SuggestType(new[] { Staged("src/Old.cs", FileChangeStatus.Deleted) }));
    }

    [Fact]
    public void SuggestType_AllRenamed_ReturnsRefactor()
    {
        Assert.Same(ConventionalCommitType.Refactor,
            ConventionalCommitSuggester.SuggestType(new[] { Staged("src/New.cs", FileChangeStatus.Renamed) }));
    }

    [Fact]
    public void SuggestType_MostlyAdditions_ReturnsFeat()
    {
        var changes = new[] { Staged("src/App.cs") };
        var numstat = new Dictionary<string, ConventionalCommitSuggester.LineStats>
        {
            ["src/App.cs"] = new(40, 2)
        };
        Assert.Same(ConventionalCommitType.Feat, ConventionalCommitSuggester.SuggestType(changes, numstat));
    }

    [Fact]
    public void SuggestType_LargeTwoWayEdit_ReturnsRefactor()
    {
        var changes = new[] { Staged("src/App.cs") };
        var numstat = new Dictionary<string, ConventionalCommitSuggester.LineStats>
        {
            ["src/App.cs"] = new(30, 25)
        };
        Assert.Same(ConventionalCommitType.Refactor, ConventionalCommitSuggester.SuggestType(changes, numstat));
    }

    [Fact]
    public void SuggestType_SmallTwoWayEdit_ReturnsFix()
    {
        var changes = new[] { Staged("src/App.cs") };
        var numstat = new Dictionary<string, ConventionalCommitSuggester.LineStats>
        {
            ["src/App.cs"] = new(3, 2)
        };
        Assert.Same(ConventionalCommitType.Fix, ConventionalCommitSuggester.SuggestType(changes, numstat));
    }

    [Fact]
    public void SuggestType_WithoutNumstat_FallsBackToChore()
    {
        // A modified file with no line counts available carries no signal.
        Assert.Same(ConventionalCommitType.Chore, ConventionalCommitSuggester.SuggestType(new[] { Staged("src/App.cs") }));
    }

    [Fact]
    public void SuggestScope_SingleTopLevelFolder_ReturnsIt()
    {
        var changes = new[] { Staged("GitHalls.App/MainWindow.xaml"), Staged("GitHalls.App/Views/DiffPage.xaml") };
        Assert.Equal("githalls.app", ConventionalCommitSuggester.SuggestScope(changes));
    }

    [Fact]
    public void SuggestScope_SpreadAcrossFolders_ReturnsNull()
    {
        var changes = new[] { Staged("GitHalls.App/MainWindow.xaml"), Staged("GitHalls.Core/Git/GitService.cs") };
        Assert.Null(ConventionalCommitSuggester.SuggestScope(changes));
    }

    [Fact]
    public void SuggestScope_NoChanges_ReturnsNull()
    {
        Assert.Null(ConventionalCommitSuggester.SuggestScope(Array.Empty<FileChange>()));
    }
}
