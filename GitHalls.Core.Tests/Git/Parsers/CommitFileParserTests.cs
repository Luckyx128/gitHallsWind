using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git.Parsers;

/// <summary>
/// The inputs here are verbatim output of
/// <c>git show --pretty=format: --raw --numstat -z</c>, captured from a real
/// repository — the -z forms of --raw and --numstat differ from each other in
/// ways only real output shows (the numstat path shares its token with the
/// counts, the raw path does not).
/// </summary>
public class CommitFileParserTests
{
    private readonly CommitFileParser _parser = new();

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        // A merge commit shows no diff at all.
        Assert.Empty(_parser.Parse(""));
    }

    [Fact]
    public void Parse_SingleModifiedFile_ReadsStatusAndCounts()
    {
        var input = ":100644 100644 9ae056e edfdbb3 M\0GitHalls.App/Controls/BranchPicker.xaml.cs\0"
                  + "5\t4\tGitHalls.App/Controls/BranchPicker.xaml.cs\0";

        var result = _parser.Parse(input);

        var file = Assert.Single(result);
        Assert.Equal("GitHalls.App/Controls/BranchPicker.xaml.cs", file.Path);
        Assert.Equal(FileChangeStatus.Modified, file.Status);
        Assert.Equal(5, file.Additions);
        Assert.Equal(4, file.Deletions);
        Assert.False(file.IsBinary);
        Assert.Null(file.OriginalPath);
    }

    [Fact]
    public void Parse_AddedAndDeleted_KeepsGitOrder()
    {
        var input = ":000000 100644 0000000 c16e265 A\0new.md\0"
                  + ":100644 000000 1f4b27c 0000000 D\0gone.cs\0"
                  + "60\t0\tnew.md\0"
                  + "0\t12\tgone.cs\0";

        var result = _parser.Parse(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("new.md", result[0].Path);
        Assert.Equal(FileChangeStatus.Added, result[0].Status);
        Assert.Equal(60, result[0].Additions);
        Assert.Equal("gone.cs", result[1].Path);
        Assert.Equal(FileChangeStatus.Deleted, result[1].Status);
        Assert.Equal(12, result[1].Deletions);
    }

    [Fact]
    public void Parse_RenameAndBinary_ReadsBothForms()
    {
        // A rename leaves the numstat path field empty and follows with the
        // source and destination as separate tokens; a binary file reports "-".
        var input = ":100644 100644 9b3c310 2569e16 M\0img.bin\0"
                  + ":100644 100644 de98044 d68dd40 R075\0velho.txt\0novo com espaço.txt\0"
                  + "-\t-\timg.bin\0"
                  + "1\t0\t\0velho.txt\0novo com espaço.txt\0";

        var result = _parser.Parse(input);

        Assert.Equal(2, result.Count);

        Assert.Equal("img.bin", result[0].Path);
        Assert.True(result[0].IsBinary);
        Assert.Equal(0, result[0].Additions);
        Assert.Equal(string.Empty, result[0].AdditionsText);

        // The destination is the file that now exists, so that is what it is keyed by.
        Assert.Equal("novo com espaço.txt", result[1].Path);
        Assert.Equal("velho.txt", result[1].OriginalPath);
        Assert.Equal(FileChangeStatus.Renamed, result[1].Status);
        Assert.Equal(1, result[1].Additions);
        Assert.Equal(0, result[1].Deletions);
        Assert.False(result[1].IsBinary);
    }

    [Fact]
    public void Parse_MissingNumstatBlock_StillListsTheFiles()
    {
        // The list is what the pane needs; counts are decoration.
        var result = _parser.Parse(":100644 100644 aaa bbb M\0src/file.cs\0");

        var file = Assert.Single(result);
        Assert.Equal("src/file.cs", file.Path);
        Assert.Equal(0, file.Additions);
        Assert.Equal(0, file.Deletions);
    }

    [Fact]
    public void Parse_TruncatedRawEntry_DropsIt()
    {
        var result = _parser.Parse(":100644 100644 aaa bbb M\0");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_MergeCommit_ReadsTheCombinedBlockThatComesLast()
    {
        // A merge inverts the usual order: numstat first (its first-parent diff),
        // then the combined raw block — "::" with one status per parent. The file
        // list is the combined block, which is what the merge itself resolved.
        var input = "28\t17\tGitHalls.App/MainWindow.xaml\0"
                  + "92\t36\tGitHalls.App/MainWindow.xaml.cs\0"
                  + "3\t1\tGitHalls.App/Views/HistorySidebarPage.xaml\0"
                  + "::100644 100644 100644 869bcfc e836ec6 0d41b82 MM\0GitHalls.App/MainWindow.xaml\0"
                  + "::100644 100644 100644 f4f0f2d d07c0a7 e98089f MM\0GitHalls.App/MainWindow.xaml.cs\0";

        var result = _parser.Parse(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("GitHalls.App/MainWindow.xaml", result[0].Path);
        Assert.Equal(FileChangeStatus.Modified, result[0].Status);
        Assert.Equal(28, result[0].Additions);
        Assert.Equal(17, result[0].Deletions);
        Assert.Equal("GitHalls.App/MainWindow.xaml.cs", result[1].Path);
    }
}
