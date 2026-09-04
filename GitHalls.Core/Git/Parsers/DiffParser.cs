using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class DiffParser
{
    public FileDiff Parse(string filePath, string diffOutput)
    {
        if (diffOutput.Contains("Binary files differ") || diffOutput.StartsWith("Binary files"))
        {
            return new FileDiff(filePath, Array.Empty<DiffLine>(), isBinary: true);
        }

        var lines = new List<DiffLine>();
        var stringLines = diffOutput.Split('\n');
        
        int? oldLineNumber = null;
        int? newLineNumber = null;

        foreach (var line in stringLines)
        {
            if (line.StartsWith("diff --git") || line.StartsWith("index ") || 
                line.StartsWith("--- ") || line.StartsWith("+++ "))
            {
                lines.Add(new DiffLine(line, DiffLineType.Header, null, null));
                continue;
            }

            if (line.StartsWith("@@ "))
            {
                ParseHunkHeader(line, out oldLineNumber, out newLineNumber);
                lines.Add(new DiffLine(line, DiffLineType.Header, null, null));
                continue;
            }

            if (line.StartsWith("+"))
            {
                lines.Add(new DiffLine(line, DiffLineType.Addition, null, newLineNumber));
                newLineNumber++;
            }
            else if (line.StartsWith("-"))
            {
                lines.Add(new DiffLine(line, DiffLineType.Deletion, oldLineNumber, null));
                oldLineNumber++;
            }
            else if (line.StartsWith(" "))
            {
                lines.Add(new DiffLine(line, DiffLineType.Context, oldLineNumber, newLineNumber));
                oldLineNumber++;
                newLineNumber++;
            }
            else if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else
            {
                // Fallback for unexpected line types, like \ No newline at end of file
                lines.Add(new DiffLine(line, DiffLineType.Context, null, null));
            }
        }

        return new FileDiff(filePath, lines);
    }

    private void ParseHunkHeader(string header, out int? oldLine, out int? newLine)
    {
        oldLine = null;
        newLine = null;
        
        // Example: @@ -1,4 +1,5 @@
        var parts = header.Split(' ');
        if (parts.Length >= 3)
        {
            var oldPart = parts[1].TrimStart('-'); // "1,4" or "1"
            var newPart = parts[2].TrimStart('+'); // "1,5" or "1"

            var oldStart = oldPart.Split(',')[0];
            var newStart = newPart.Split(',')[0];

            if (int.TryParse(oldStart, out var o)) oldLine = o;
            if (int.TryParse(newStart, out var n)) newLine = n;
        }
    }
}
