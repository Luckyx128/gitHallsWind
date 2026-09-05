using System.Text;
using GitHalls.App.Themes;
using GitHalls.Core.Diff;
using GitHalls.Core.Models;

namespace GitHalls.App.Services;

public sealed class HighlightedDiffLine
{
    public required DiffLineType Type { get; init; }
    /// <summary>Code content only — no trailing newline, no '+'/'-' prefix.</summary>
    public required IReadOnlyList<StyledRun> Runs { get; init; }
    public int? OldLineNumber { get; init; }
    public int? NewLineNumber { get; init; }
    public required string RawText { get; init; }
}

public sealed class HighlightedDiff
{
    public required string Path { get; init; }
    public required IReadOnlyList<HighlightedDiffLine> Lines { get; init; }
    /// <summary>The whole diff as plain text, for "Copy Entire Diff".</summary>
    public required string PlainText { get; init; }
    public required int MaxOldLineNumber { get; init; }
    public required int MaxNewLineNumber { get; init; }
}

/// <summary>
/// Reconstructs the "old" and "new" side of a FileDiff, highlights each once,
/// then maps every line back to its highlighted content.
///
/// Highlighting each diff line on its own would be simpler and wrong: a block
/// comment, a multi-line string or an unterminated brace only makes sense in
/// the context of the surrounding file, so a line-at-a-time pass mis-colours
/// everything after the construct opens. Port of DiffHighlightMapper.swift.
/// </summary>
public static class DiffHighlightMapper
{
    public static HighlightedDiff Make(FileDiff diff, IDiffHighlighter highlighter)
    {
        var language = SyntaxLanguage.ForPath(diff.FilePath);

        var oldRaw = diff.Lines.Where(l => l.Type is DiffLineType.Context or DiffLineType.Deletion).Select(l => l.Content).ToList();
        var newRaw = diff.Lines.Where(l => l.Type is DiffLineType.Context or DiffLineType.Addition).Select(l => l.Content).ToList();

        var oldLines = Highlight(oldRaw, language, highlighter);
        var newLines = Highlight(newRaw, language, highlighter);

        var mapped = new List<HighlightedDiffLine>(diff.Lines.Count);
        var plain = new StringBuilder();

        int oldIndex = 0;
        int newIndex = 0;
        int maxOld = 0;
        int maxNew = 0;

        foreach (var line in diff.Lines)
        {
            IReadOnlyList<StyledRun> runs;

            switch (line.Type)
            {
                case DiffLineType.HunkHeader:
                    runs = new[] { new StyledRun(line.Content, DiffTokenKind.Plain) };
                    break;

                case DiffLineType.Deletion:
                    runs = At(oldLines, oldIndex++) ?? Plain(line.Content);
                    break;

                case DiffLineType.Addition:
                    runs = At(newLines, newIndex++) ?? Plain(line.Content);
                    break;

                default: // Context advances both sides
                    runs = At(newLines, newIndex) ?? Plain(line.Content);
                    oldIndex++;
                    newIndex++;
                    break;
            }

            if (line.OldLineNumber is int old && old > maxOld) maxOld = old;
            if (line.NewLineNumber is int @new && @new > maxNew) maxNew = @new;

            mapped.Add(new HighlightedDiffLine
            {
                Type = line.Type,
                Runs = runs,
                OldLineNumber = line.OldLineNumber,
                NewLineNumber = line.NewLineNumber,
                RawText = line.Content
            });

            plain.Append(line.Content).Append('\n');
        }

        return new HighlightedDiff
        {
            Path = diff.FilePath,
            Lines = mapped,
            PlainText = plain.ToString(),
            MaxOldLineNumber = maxOld,
            MaxNewLineNumber = maxNew
        };
    }

    private static IReadOnlyList<IReadOnlyList<StyledRun>> Highlight(List<string> raw, string? language, IDiffHighlighter highlighter)
    {
        if (raw.Count == 0) return Array.Empty<IReadOnlyList<StyledRun>>();

        var highlighted = highlighter.HighlightLines(string.Join('\n', raw), language);

        // The contract is one entry per line; anything else would shift every
        // line's colouring against its content.
        return highlighted.Count == raw.Count
            ? highlighted
            : raw.Select(Plain).ToList();
    }

    private static IReadOnlyList<StyledRun>? At(IReadOnlyList<IReadOnlyList<StyledRun>> lines, int index)
        => index >= 0 && index < lines.Count ? lines[index] : null;

    private static IReadOnlyList<StyledRun> Plain(string text) => new[] { new StyledRun(text, DiffTokenKind.Plain) };
}
