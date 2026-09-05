using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git.Parsers;

public class DiffParserTests
{
    private readonly DiffParser _parser = new();

    [Fact]
    public void Parse_UnifiedDiff_KeepsOnlyHunkHeaderAndContent()
    {
        var diff = string.Join('\n',
            "diff --git a/file.cs b/file.cs",
            "index 1234567..89abcdef 100644",
            "--- a/file.cs",
            "+++ b/file.cs",
            "@@ -1,3 +1,4 @@",
            " using System;",
            "-var old = 1;",
            "+var newVar = 2;",
            "+var another = 3;",
            " class Program {}");

        var result = _parser.Parse("file.cs", diff);

        Assert.False(result.IsBinary);
        Assert.Equal("file.cs", result.FilePath);

        // The git file headers are dropped — the file name is shown by the UI
        // itself. What remains: 1 hunk header + 2 context + 1 deletion + 2 additions.
        Assert.Equal(6, result.Lines.Count);
        Assert.Equal(DiffLineType.HunkHeader, result.Lines[0].Type);

        var context = result.Lines[1];
        Assert.Equal(DiffLineType.Context, context.Type);
        Assert.Equal("using System;", context.Content);
        Assert.Equal(1, context.OldLineNumber);
        Assert.Equal(1, context.NewLineNumber);

        var deletion = result.Lines[2];
        Assert.Equal(DiffLineType.Deletion, deletion.Type);
        Assert.Equal("var old = 1;", deletion.Content);
        Assert.Equal(2, deletion.OldLineNumber);
        Assert.Null(deletion.NewLineNumber);

        var addition = result.Lines[3];
        Assert.Equal(DiffLineType.Addition, addition.Type);
        Assert.Equal("var newVar = 2;", addition.Content);
        Assert.Null(addition.OldLineNumber);
        Assert.Equal(2, addition.NewLineNumber);

        Assert.Equal(2, result.Additions);
        Assert.Equal(1, result.Deletions);
    }

    [Fact]
    public void Parse_HunkHeader_UsesFriendlyLabelWithTrailingContext()
    {
        var diff = "@@ -10,3 +42,4 @@ public void Run()\n context\n";

        var result = _parser.Parse("file.cs", diff);

        Assert.Equal("Line 42 · public void Run()", result.Lines[0].Content);
        Assert.Equal(42, result.Lines[1].NewLineNumber);
        Assert.Equal(10, result.Lines[1].OldLineNumber);
    }

    [Fact]
    public void Parse_HunkHeader_WithoutTrailingContext_ShowsLineOnly()
    {
        var result = _parser.Parse("file.cs", "@@ -1,3 +1,4 @@\n context\n");
        Assert.Equal("Line 1", result.Lines[0].Content);
    }

    [Fact]
    public void Parse_ExtendedHeaders_AreNotTreatedAsContent()
    {
        // Everything before the first "@@" is git's extended header. Without the
        // insideHunk guard these become bogus context lines.
        var diff = string.Join('\n',
            "diff --git a/old.cs b/new.cs",
            "similarity index 92%",
            "rename from old.cs",
            "rename to new.cs",
            "index 1234567..89abcdef 100644",
            "@@ -1 +1 @@",
            "-a",
            "+b");

        var result = _parser.Parse("new.cs", diff);

        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(DiffLineType.HunkHeader, result.Lines[0].Type);
        Assert.Equal(DiffLineType.Deletion, result.Lines[1].Type);
        Assert.Equal(DiffLineType.Addition, result.Lines[2].Type);
    }

    [Fact]
    public void Parse_BinaryDiff_SetsIsBinaryTrue()
    {
        var diff = "diff --git a/image.png b/image.png\nBinary files a/image.png and b/image.png differ\n";

        var result = _parser.Parse("image.png", diff);

        Assert.True(result.IsBinary);
        Assert.Equal(DiffParser.BinaryFileText, Assert.Single(result.Lines).Content);
    }

    [Fact]
    public void Parse_MergeDiff_IsReportedInsteadOfMisparsed()
    {
        // The combined format uses a multi-character prefix per line, which this
        // parser would read as content and corrupt.
        var diff = "@@@ -1,2 -1,2 +1,2 @@@\n- a\n +b\n++c\n";

        var result = _parser.Parse("file.cs", diff);

        Assert.Equal(DiffParser.MergeDiffText, Assert.Single(result.Lines).Content);
    }

    [Fact]
    public void Parse_NoNewlineMarker_IsSkipped()
    {
        var diff = "@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+b\n\\ No newline at end of file\n";

        var result = _parser.Parse("file.txt", diff);

        Assert.Equal(3, result.Lines.Count);
        Assert.DoesNotContain(result.Lines, l => l.Content.StartsWith("\\"));
    }

    [Fact]
    public void Parse_EmptyDiff_ReportsNoContentChanges()
    {
        // A pure rename or a mode change produces no hunks at all — which is not
        // the same as having nothing to show.
        var result = _parser.Parse("file.cs", "diff --git a/file.cs b/file.cs\nold mode 100644\nnew mode 100755\n");

        Assert.Equal(DiffParser.NoContentChangesText, Assert.Single(result.Lines).Content);
    }

    [Fact]
    public void SyntheticAllAdditions_NumbersEveryLine()
    {
        var result = _parser.SyntheticAllAdditions("new.txt", "first\nsecond\nthird");

        Assert.Equal(DiffLineType.HunkHeader, result.Lines[0].Type);
        Assert.Equal(3, result.Additions);
        Assert.Equal("first", result.Lines[1].Content);
        Assert.Equal(1, result.Lines[1].NewLineNumber);
        Assert.Equal(3, result.Lines[3].NewLineNumber);
    }
}
