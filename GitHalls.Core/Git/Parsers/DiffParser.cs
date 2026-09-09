using System.Text;
using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class DiffParser
{
    public const string BinaryFileText = "Binary file not shown";
    public const string MergeDiffText = "Merge diff not supported";
    public const string NoContentChangesText = "No content changes";

    public FileDiff Parse(string filePath, string diffOutput) =>
        Parse(filePath, diffOutput, DiffSide.Combined);

    /// <summary>
    /// Parses one file's unified diff. Everything a patch has to be rebuilt from
    /// — the preamble, the raw "@@" ranges, each line's untouched bytes and the
    /// no-newline markers — is kept alongside what is displayed, so staging a
    /// selection never needs a second call to git.
    /// </summary>
    public FileDiff Parse(string filePath, string diffOutput, DiffSide side)
    {
        if (string.IsNullOrEmpty(diffOutput))
        {
            return new FileDiff(filePath, new[] { Notice(NoContentChangesText) }, side: side);
        }

        var lines = new List<DiffLine>();
        var hunks = new List<DiffHunk>();
        var preamble = new StringBuilder();
        var stringLines = diffOutput.Split('\n');

        int oldLineNumber = 0;
        int newLineNumber = 0;
        bool insideHunk = false;

        // Filled in when the hunk ends, since its content range is only known then.
        int pendingHeaderIndex = -1;
        int pendingOldStart = 0, pendingOldCount = 0, pendingNewStart = 0, pendingNewCount = 0;

        void CloseHunk()
        {
            if (pendingHeaderIndex < 0) return;

            var first = pendingHeaderIndex + 1;
            var last = lines.Count - 1;
            if (last < first) { first = -1; last = -1; }

            hunks.Add(new DiffHunk(
                pendingOldStart, pendingOldCount, pendingNewStart, pendingNewCount,
                pendingHeaderIndex, first, last));

            pendingHeaderIndex = -1;
        }

        foreach (var line in stringLines)
        {
            if (line.StartsWith("Binary files ") && line.EndsWith(" differ"))
            {
                return new FileDiff(filePath, new[] { Notice(BinaryFileText) }, isBinary: true, side: side);
            }

            // A merge commit diff ("@@@ -a,b -c,d +e,f @@@") uses the combined
            // format — several "@@" markers and a multi-character prefix per
            // line — which this parser doesn't understand. Better to say so
            // than to parse it and corrupt the content.
            if (line.StartsWith("@@@"))
            {
                return new FileDiff(filePath, new[] { Notice(MergeDiffText) }, side: side);
            }

            if (line.StartsWith("@@"))
            {
                CloseHunk();

                ParseHunkHeader(line, ref oldLineNumber, ref newLineNumber, out var counts, out var label);

                pendingHeaderIndex = lines.Count;
                pendingOldStart = oldLineNumber;
                pendingOldCount = counts.Old;
                pendingNewStart = newLineNumber;
                pendingNewCount = counts.New;

                lines.Add(new DiffLine(label, DiffLineType.HunkHeader, null, null, hunkIndex: hunks.Count));
                insideHunk = true;
                continue;
            }

            // Anything before the first "@@" is git's extended header
            // (diff --git, index, ---, +++, new/deleted file mode,
            // rename from/to, similarity index...). It is not content, but a
            // patch without it is not a patch git will read.
            if (!insideHunk)
            {
                if (line.Length > 0) preamble.Append(line).Append('\n');
                continue;
            }

            // "\ No newline at end of file" is a git marker, not file content —
            // it describes the line above it.
            if (line.StartsWith("\\"))
            {
                if (lines.Count > 0) lines[^1] = lines[^1].WithNoNewlineAtEof();
                continue;
            }

            if (line.Length == 0) continue;

            // The CR of a CRLF file is part of the bytes, not of the text: it
            // stays in RawLine and is kept out of what gets displayed.
            var content = line.Substring(1).TrimEnd('\r');
            var hunkIndex = hunks.Count;

            switch (line[0])
            {
                case '+':
                    lines.Add(new DiffLine(content, DiffLineType.Addition, null, newLineNumber, hunkIndex, line));
                    newLineNumber++;
                    break;
                case '-':
                    lines.Add(new DiffLine(content, DiffLineType.Deletion, oldLineNumber, null, hunkIndex, line));
                    oldLineNumber++;
                    break;
                default:
                    lines.Add(new DiffLine(content, DiffLineType.Context, oldLineNumber, newLineNumber, hunkIndex, line));
                    oldLineNumber++;
                    newLineNumber++;
                    break;
            }
        }

        CloseHunk();

        if (lines.Count == 0)
        {
            // Mode change or a pure rename with no content change — that is not
            // the same thing as "no diff at all".
            lines.Add(Notice(NoContentChangesText));
        }

        return new FileDiff(filePath, lines, hunks: hunks, preambleText: preamble.ToString(), side: side);
    }

    /// <summary>A standalone message, belonging to no hunk.</summary>
    private static DiffLine Notice(string text) => new(text, DiffLineType.HunkHeader, null, null);

    /// <summary>
    /// Reads "@@ -1,4 +1,5 @@ optional context" into the two starting line
    /// numbers and their counts, and builds the friendly label shown in place of
    /// the raw syntax.
    /// </summary>
    private static void ParseHunkHeader(
        string header, ref int oldLine, ref int newLine, out (int Old, int New) counts, out string label)
    {
        // Everything between the first and second "@@" is the range body;
        // whatever follows the second one is the enclosing-context hint.
        var bodyStart = 2;
        var bodyEnd = header.IndexOf("@@", bodyStart, StringComparison.Ordinal);
        var body = bodyEnd < 0 ? header.Substring(bodyStart) : header.Substring(bodyStart, bodyEnd - bodyStart);
        var trailingContext = bodyEnd < 0 ? string.Empty : header.Substring(bodyEnd + 2).Trim();

        // An omitted count means 1: "@@ -5 +5,2 @@" is legal.
        var oldCount = 1;
        var newCount = 1;

        var parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length < 2) continue;
            var fields = part.Substring(1).Split(',');
            if (!int.TryParse(fields[0], out var value)) continue;

            var count = 1;
            if (fields.Length > 1 && !int.TryParse(fields[1], out count)) continue;

            if (part[0] == '-') { oldLine = value; oldCount = count; }
            else if (part[0] == '+') { newLine = value; newCount = count; }
        }

        counts = (oldCount, newCount);

        var builder = new StringBuilder("Line ").Append(newLine);
        if (trailingContext.Length > 0) builder.Append(" · ").Append(trailingContext);
        label = builder.ToString();
    }

    /// <summary>
    /// Builds an all-additions diff for an untracked file, whose content git
    /// won't produce a diff for. It carries no preamble and no raw lines, so it
    /// cannot be staged line by line — an untracked file is staged whole.
    /// </summary>
    public FileDiff SyntheticAllAdditions(string filePath, string content)
    {
        var contentLines = content.Split('\n');
        var lines = new List<DiffLine>(contentLines.Length + 1)
        {
            Notice("Line 1")
        };

        for (int i = 0; i < contentLines.Length; i++)
        {
            lines.Add(new DiffLine(contentLines[i].TrimEnd('\r'), DiffLineType.Addition, null, i + 1));
        }

        return new FileDiff(filePath, lines);
    }
}
