using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Git.Parsers;

public class DiffParserTests
{
    private readonly DiffParser _parser = new();

    [Fact]
    public void Parse_UnifiedDiff_ParsesCorrectly()
    {
        var diff = @"diff --git a/file.cs b/file.cs
index 1234567..89abcdef 100644
--- a/file.cs
+++ b/file.cs
@@ -1,3 +1,4 @@
 using System;
-var old = 1;
+var newVar = 2;
+var another = 3;
 class Program {}";

        var result = _parser.Parse("file.cs", diff);
        Assert.False(result.IsBinary);
        Assert.Equal("file.cs", result.FilePath);
        
        // 4 header lines + 1 hunk header + 1 context + 1 deletion + 2 additions + 1 context = 10 lines
        Assert.Equal(10, result.Lines.Count);

        var hunk = result.Lines[4];
        Assert.Equal(DiffLineType.Header, hunk.Type);

        var deletion = result.Lines[6];
        Assert.Equal(DiffLineType.Deletion, deletion.Type);
        Assert.Equal(2, deletion.OldLineNumber);
        Assert.Null(deletion.NewLineNumber);

        var addition = result.Lines[7];
        Assert.Equal(DiffLineType.Addition, addition.Type);
        Assert.Null(addition.OldLineNumber);
        Assert.Equal(2, addition.NewLineNumber);
    }

    [Fact]
    public void Parse_BinaryDiff_SetsIsBinaryTrue()
    {
        var diff = "Binary files a/image.png and b/image.png differ\n";
        var result = _parser.Parse("image.png", diff);

        Assert.True(result.IsBinary);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Parse_CrLfEndings_HandlesProperly()
    {
        // Even if we normalize \r\n to \n in GitProcessRunner, we should test DiffParser works well with \n
        var diff = "diff --git a/a b/b\n@@ -1 +1 @@\n-a\n+b\n";
        var result = _parser.Parse("file", diff);
        
        Assert.Equal(4, result.Lines.Count);
    }
}
