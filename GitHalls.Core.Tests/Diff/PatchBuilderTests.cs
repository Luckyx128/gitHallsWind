using GitHalls.Core.Diff;
using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;
using Xunit;

namespace GitHalls.Core.Tests.Diff;

/// <summary>
/// Every diff here is real output from git, and every expected patch was fed
/// back to "git apply --cached" before being written down. The counts in a hunk
/// header are the whole game: git rejects the patch outright if they are wrong,
/// so they are asserted literally rather than described.
/// </summary>
public class PatchBuilderTests
{
    private readonly DiffParser _parser = new();

    private FileDiff Parse(DiffSide side, params string[] lines) =>
        _parser.Parse("file.txt", string.Join('\n', lines), side);

    private static HashSet<int> IndicesOf(FileDiff diff, DiffLineType type, params string[] contents)
    {
        var wanted = new HashSet<string>(contents);
        var found = new HashSet<int>();
        for (int i = 0; i < diff.Lines.Count; i++)
        {
            if (diff.Lines[i].Type == type && wanted.Contains(diff.Lines[i].Content)) found.Add(i);
        }

        return found;
    }

    private static string[] Preamble => new[]
    {
        "diff --git a/file.txt b/file.txt",
        "index 1234567..89abcde 100644",
        "--- a/file.txt",
        "+++ b/file.txt"
    };

    // MARK: - Forward (staging)

    [Fact]
    public void Build_UnselectedAddition_IsDroppedFromThePatch()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,2 +1,4 @@",
            " one",
            "+first",
            "+second",
            " two"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Addition, "first"), PatchDirection.Forward);

        Assert.Equal(string.Join('\n',
            "diff --git a/file.txt b/file.txt",
            "index 1234567..89abcde 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,2 +1,3 @@",
            " one",
            "+first",
            " two",
            ""), patch);
    }

    [Fact]
    public void Build_UnselectedDeletion_BecomesContextSoItSurvivesInTheIndex()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,3 +1,1 @@",
            "-one",
            "-two",
            " three"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Deletion, "two"), PatchDirection.Forward);

        // "one" is still in the index, so the patch has to see it on both sides.
        Assert.Equal(string.Join('\n',
            "diff --git a/file.txt b/file.txt",
            "index 1234567..89abcde 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,3 +1,2 @@",
            " one",
            "-two",
            " three",
            ""), patch);
    }

    [Fact]
    public void Build_HunkWithNothingSelected_IsOmittedEntirely()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,1 +1,2 @@",
            " one",
            "+added here",
            "@@ -10,1 +11,2 @@",
            " ten",
            "+added there"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Addition, "added there"), PatchDirection.Forward);

        Assert.NotNull(patch);
        Assert.DoesNotContain("added here", patch);
        Assert.Single(patch!.Split("@@ -").Skip(1));
    }

    [Fact]
    public void Build_PartiallyStagedFirstHunk_ShiftsTheNextHunksNewStart()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,1 +1,4 @@",
            " one",
            "+a",
            "+b",
            "+c",
            "@@ -20,1 +23,2 @@",
            " twenty",
            "+d"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Addition, "a", "d"), PatchDirection.Forward);

        // git said +23 because three lines went in above; this patch only puts
        // one there, so the second hunk lands at +21.
        Assert.Contains("@@ -1,1 +1,2 @@", patch);
        Assert.Contains("@@ -20,1 +21,2 @@", patch);
    }

    [Fact]
    public void Build_NoNewlineMarker_IsCarriedWithItsLine()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,3 +1,3 @@",
            " alpha",
            " bravo",
            "-charlie",
            "\\ No newline at end of file",
            "+delta",
            "\\ No newline at end of file"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, PatchBuilder.AllChangedLines(diff), PatchDirection.Forward);

        // Two markers, each still behind the line it describes. Losing one makes
        // git append a newline the file never had.
        Assert.Contains("-charlie\n\\ No newline at end of file\n+delta\n\\ No newline at end of file\n", patch);
    }

    [Fact]
    public void Build_CrlfContent_KeepsTheCarriageReturn()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,2 +1,2 @@",
            " one\r",
            "-two\r",
            "+dois\r"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, PatchBuilder.AllChangedLines(diff), PatchDirection.Forward);

        // Stripping the CR would produce a patch that no longer matches the
        // index, and git would reject it with "patch does not apply".
        Assert.Contains("+dois\r\n", patch);
        Assert.Contains(" one\r\n", patch);
        Assert.Equal("dois", diff.Lines[^1].Content);
    }

    [Fact]
    public void Build_EveryDeletionSelected_NumbersTheEmptiedRangeAsZero()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,2 +0,0 @@",
            "-one",
            "-two"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, PatchBuilder.AllChangedLines(diff), PatchDirection.Forward);

        // A unified diff numbers an empty range by the line it sits after.
        Assert.Contains("@@ -1,2 +0,0 @@", patch);
    }

    [Fact]
    public void Build_AdditionIntoAnEmptyOldRange_KeepsGitsZeroNumbering()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -0,0 +1,3 @@",
            "+one",
            "+two",
            "+three"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Addition, "two"), PatchDirection.Forward);

        Assert.Contains("@@ -0,0 +1,1 @@", patch);
    }

    // MARK: - Reverse (unstaging)

    [Fact]
    public void Build_Reverse_UnselectedAdditionBecomesContextAndDeletionIsDropped()
    {
        var diff = Parse(DiffSide.Index, Preamble.Concat(new[]
        {
            "@@ -1,3 +1,3 @@",
            " one",
            "+kept",
            "+taken back",
            "-gone",
            " two"
        }).ToArray());

        var patch = PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Addition, "taken back"), PatchDirection.Reverse);

        Assert.Equal(string.Join('\n',
            "diff --git a/file.txt b/file.txt",
            "index 1234567..89abcde 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,3 +1,4 @@",
            " one",
            " kept",
            "+taken back",
            " two",
            ""), patch);
    }

    // MARK: - Nothing to do

    [Fact]
    public void Build_EmptySelection_ReturnsNull()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[] { "@@ -1,1 +1,2 @@", " one", "+two" }).ToArray());

        Assert.Null(PatchBuilder.Build(diff, new HashSet<int>(), PatchDirection.Forward));
    }

    [Fact]
    public void Build_ContextOnlySelection_ReturnsNullBecauseNothingWouldChange()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[] { "@@ -1,1 +1,2 @@", " one", "+two" }).ToArray());

        Assert.Null(PatchBuilder.Build(diff, IndicesOf(diff, DiffLineType.Context, "one"), PatchDirection.Forward));
    }

    [Fact]
    public void Build_CombinedDiff_ReturnsNullBecauseItCannotBeAppliedToTheIndex()
    {
        var diff = Parse(DiffSide.Combined, Preamble.Concat(new[] { "@@ -1,1 +1,2 @@", " one", "+two" }).ToArray());

        Assert.Null(PatchBuilder.Build(diff, PatchBuilder.AllChangedLines(diff), PatchDirection.Forward));
    }

    [Fact]
    public void Build_SynthesizedDiff_ReturnsNullBecauseItHasNoPatchToRebuild()
    {
        var diff = _parser.SyntheticAllAdditions("new.txt", "one\ntwo\n");

        Assert.Null(PatchBuilder.Build(diff, PatchBuilder.AllChangedLines(diff), PatchDirection.Forward));
    }

    // MARK: - Selection helpers

    [Fact]
    public void ChangedLinesOf_LeavesContextOut()
    {
        var diff = Parse(DiffSide.WorkingTree, Preamble.Concat(new[]
        {
            "@@ -1,2 +1,3 @@",
            " one",
            "-two",
            "+dois",
            "+tres"
        }).ToArray());

        var lines = PatchBuilder.ChangedLinesOf(diff, diff.Hunks[0]);

        // Selecting context would inflate what the action bar reports and
        // change nothing in the patch.
        Assert.Equal(3, lines.Count);
        Assert.All(lines, i => Assert.True(PatchBuilder.IsSelectable(diff.Lines[i])));
    }
}
