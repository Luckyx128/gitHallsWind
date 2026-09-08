using GitHalls.Core.Diff;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Diff;

public class SideBySideDiffTests
{
    private static DiffLine Context(string text, int old, int @new) => new(text, DiffLineType.Context, old, @new);
    private static DiffLine Removed(string text, int old) => new(text, DiffLineType.Deletion, old, null);
    private static DiffLine Added(string text, int @new) => new(text, DiffLineType.Addition, null, @new);

    [Fact]
    public void Split_PairsARemovalWithTheAdditionThatReplacedIt()
    {
        var diff = new FileDiff("a.cs", new[]
        {
            Context("first", 1, 1),
            Removed("was", 2),
            Added("is", 2),
            Context("last", 3, 3)
        });

        var (left, right) = SideBySideDiff.Split(diff);

        Assert.Equal(3, left.Lines.Count);
        Assert.Equal(3, right.Lines.Count);

        // Row 1 holds the change on both sides — that alignment is the point.
        Assert.Equal("was", left.Lines[1].Content);
        Assert.Equal("is", right.Lines[1].Content);
        Assert.Equal(DiffLineType.Deletion, left.Lines[1].Type);
        Assert.Equal(DiffLineType.Addition, right.Lines[1].Type);
    }

    [Fact]
    public void Split_PadsTheShorterSideSoBothColumnsStayInStep()
    {
        var diff = new FileDiff("a.cs", new[]
        {
            Removed("gone", 1),
            Added("one", 1),
            Added("two", 2),
            Added("three", 3),
            Context("tail", 2, 4)
        });

        var (left, right) = SideBySideDiff.Split(diff);

        Assert.Equal(left.Lines.Count, right.Lines.Count);
        Assert.Equal(4, left.Lines.Count);

        Assert.Equal("gone", left.Lines[0].Content);
        Assert.Equal(string.Empty, left.Lines[1].Content);
        Assert.Equal(string.Empty, left.Lines[2].Content);
        Assert.Equal("tail", left.Lines[3].Content);

        Assert.Equal("three", right.Lines[2].Content);
        Assert.Equal("tail", right.Lines[3].Content);
    }

    [Fact]
    public void Split_KeepsOnlyEachSidesOwnLineNumbers()
    {
        var diff = new FileDiff("a.cs", new[] { Context("same", 7, 9) });

        var (left, right) = SideBySideDiff.Split(diff);

        Assert.Equal(7, left.Lines[0].OldLineNumber);
        Assert.Null(left.Lines[0].NewLineNumber);
        Assert.Null(right.Lines[0].OldLineNumber);
        Assert.Equal(9, right.Lines[0].NewLineNumber);
    }

    [Fact]
    public void Split_PutsHunkHeadersOnBothSides()
    {
        var diff = new FileDiff("a.cs", new[]
        {
            new DiffLine("Lines 1 to 4", DiffLineType.HunkHeader, null, null),
            Removed("x", 1),
            Added("y", 1)
        });

        var (left, right) = SideBySideDiff.Split(diff);

        Assert.Equal(DiffLineType.HunkHeader, left.Lines[0].Type);
        Assert.Equal(DiffLineType.HunkHeader, right.Lines[0].Type);
    }

    [Theory]
    [InlineData(DiffLineType.Addition, true)]
    [InlineData(DiffLineType.Context, false)]
    [InlineData(DiffLineType.HunkHeader, false)]
    public void HasTwoSides_IsFalseWhenThereIsNothingToCompare(DiffLineType type, bool expected)
    {
        // "Binary file not shown" and "No content changes" arrive as a lone
        // notice; showing it twice, once per column, says nothing extra.
        var diff = new FileDiff("a.cs", new[] { new DiffLine("text", type, null, null) });

        Assert.Equal(expected, SideBySideDiff.HasTwoSides(diff));
    }

    [Fact]
    public void HasTwoSides_IsFalseForABinaryFile()
    {
        var diff = new FileDiff("a.png", new[] { Added("data", 1) }, isBinary: true);

        Assert.False(SideBySideDiff.HasTwoSides(diff));
    }
}
