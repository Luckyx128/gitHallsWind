using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class DiffParser
{
    public const string BinaryFileText = "Binary file not shown";
    public const string MergeDiffText = "Merge diff not supported";
    public const string NoContentChangesText = "No content changes";

    public FileDiff Parse(string filePath, string diffOutput)
    {
        if (string.IsNullOrEmpty(diffOutput))
        {
            return new FileDiff(filePath, new[] { HunkHeader(NoContentChangesText) });
        }

        var lines = new List<DiffLine>();
        var stringLines = diffOutput.Split('\n');

        int oldLineNumber = 0;
        int newLineNumber = 0;
        bool insideHunk = false;

        foreach (var line in stringLines)
        {
            if (line.StartsWith("Binary files ") && line.EndsWith(" differ"))
            {
                return new FileDiff(filePath, new[] { HunkHeader(BinaryFileText) }, isBinary: true);
            }

            // A merge commit diff ("@@@ -a,b -c,d +e,f @@@") uses the combined
            // format — several "@@" markers and a multi-character prefix per
            // line — which this parser doesn't understand. Better to say so
            // than to parse it and corrupt the content.
            if (line.StartsWith("@@@"))
            {
                return new FileDiff(filePath, new[] { HunkHeader(MergeDiffText) });
            }

            if (line.StartsWith("@@"))
            {
                ParseHunkHeader(line, ref oldLineNumber, ref newLineNumber, out var label);
                lines.Add(HunkHeader(label));
                insideHunk = true;
                continue;
            }

            // Anything before the first "@@" is git's extended header
            // (diff --git, index, ---, +++, new/deleted file mode,
            // rename from/to, similarity index...) — skip it.
            if (!insideHunk) continue;

            // "\ No newline at end of file" is a git marker, not file content.
            if (line.StartsWith("\\")) continue;

            if (line.Length == 0) continue;

            switch (line[0])
            {
                case '+':
                    lines.Add(new DiffLine(line.Substring(1), DiffLineType.Addition, null, newLineNumber));
                    newLineNumber++;
                    break;
                case '-':
                    lines.Add(new DiffLine(line.Substring(1), DiffLineType.Deletion, oldLineNumber, null));
                    oldLineNumber++;
                    break;
                default:
                    lines.Add(new DiffLine(line.Substring(1), DiffLineType.Context, oldLineNumber, newLineNumber));
                    oldLineNumber++;
                    newLineNumber++;
                    break;
            }
        }

        if (lines.Count == 0)
        {
            // Mode change or a pure rename with no content change — that is not
            // the same thing as "no diff at all".
            lines.Add(HunkHeader(NoContentChangesText));
        }

        return new FileDiff(filePath, lines);
    }

    private static DiffLine HunkHeader(string text) => new(text, DiffLineType.HunkHeader, null, null);

    /// <summary>
    /// Reads "@@ -1,4 +1,5 @@ optional context" into the two starting line
    /// numbers, and builds the friendly label shown in place of the raw syntax.
    /// </summary>
    private static void ParseHunkHeader(string header, ref int oldLine, ref int newLine, out string label)
    {
        // Everything between the first and second "@@" is the range body;
        // whatever follows the second one is the enclosing-context hint.
        var bodyStart = 2;
        var bodyEnd = header.IndexOf("@@", bodyStart, StringComparison.Ordinal);
        var body = bodyEnd < 0 ? header.Substring(bodyStart) : header.Substring(bodyStart, bodyEnd - bodyStart);
        var trailingContext = bodyEnd < 0 ? string.Empty : header.Substring(bodyEnd + 2).Trim();

        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length < 2) continue;
            var start = part.Substring(1).Split(',')[0];
            if (!int.TryParse(start, out var value)) continue;

            if (part[0] == '-') oldLine = value;
            else if (part[0] == '+') newLine = value;
        }

        var builder = new StringBuilder("Line ").Append(newLine);
        if (trailingContext.Length > 0) builder.Append(" · ").Append(trailingContext);
        label = builder.ToString();
    }

    /// <summary>
    /// Builds an all-additions diff for an untracked file, whose content git
    /// won't produce a diff for.
    /// </summary>
    public FileDiff SyntheticAllAdditions(string filePath, string content)
    {
        var contentLines = content.Split('\n');
        var lines = new List<DiffLine>(contentLines.Length + 1)
        {
            HunkHeader("Line 1")
        };

        for (int i = 0; i < contentLines.Length; i++)
        {
            lines.Add(new DiffLine(contentLines[i].TrimEnd('\r'), DiffLineType.Addition, null, i + 1));
        }

        return new FileDiff(filePath, lines);
    }
}
