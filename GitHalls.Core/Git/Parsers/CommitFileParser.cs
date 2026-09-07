using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

/// <summary>
/// Reads <c>git show --raw --numstat -z</c>: the file list of a whole commit,
/// with its line counts, in one call.
///
/// The file list comes from the raw entries; numstat only fills in the counts.
/// Which block git writes first is not fixed — an ordinary commit leads with raw,
/// a merge leads with numstat and then reports its combined diff — so the two are
/// read in whatever order they arrive.
///
/// -z is not a detail: without it git quotes any path with an accent or a space,
/// and a quoted path is not one git will accept back in a later "show -- path".
/// </summary>
public class CommitFileParser
{
    public IReadOnlyList<CommitFile> Parse(string gitOutput)
    {
        if (string.IsNullOrEmpty(gitOutput)) return Array.Empty<CommitFile>();

        var tokens = gitOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var entries = new List<Entry>();
        var stats = new Dictionary<string, Stat>(StringComparer.Ordinal);
        var index = 0;

        while (index < tokens.Length)
        {
            var token = tokens[index++];

            // A raw entry: ":<mode> <mode> <sha> <sha> <status>", then its path.
            // A merge reports a combined one instead, "::" and a status per parent
            // ("MM"); those never carry a rename's second path.
            if (token.StartsWith(':'))
            {
                var status = MapStatus(StatusLetter(token));
                var isCombined = token.Length > 1 && token[1] == ':';
                var hasTwoPaths = !isCombined && status is FileChangeStatus.Renamed or FileChangeStatus.Copied;

                // Truncated output: stop rather than read a path that isn't there.
                if (index + (hasTwoPaths ? 1 : 0) >= tokens.Length) break;

                var original = hasTwoPaths ? tokens[index++] : null;
                entries.Add(new Entry(tokens[index++], original, status));
                continue;
            }

            // A numstat entry: "<additions>\t<deletions>\t<path>" in one token,
            // except for a rename, which leaves the path field empty and follows
            // with the source and destination as two tokens of their own.
            // Limit of 3: a path may itself contain a tab, and -z never quotes it.
            var fields = token.Split('\t', 3);
            if (fields.Length != 3) continue;

            // A binary file reports "-" for both counts.
            var isBinary = fields[0] == "-" || fields[1] == "-";
            int.TryParse(fields[0], out var additions);
            int.TryParse(fields[1], out var deletions);

            var path = fields[2];
            if (path.Length == 0)
            {
                if (index + 1 >= tokens.Length) break;
                index++;                    // the source path
                path = tokens[index++];     // the destination, which the entry is keyed by
            }

            stats[path] = new Stat(additions, deletions, isBinary);
        }

        var files = new List<CommitFile>(entries.Count);
        foreach (var entry in entries)
        {
            stats.TryGetValue(entry.Path, out var stat);
            files.Add(new CommitFile(entry.Path, entry.Status, stat.Additions, stat.Deletions, stat.IsBinary, entry.OriginalPath));
        }

        return files;
    }

    /// <summary>The status is the last field of the raw header, e.g. "M", "R100" or "MM".</summary>
    private static char StatusLetter(string rawHeader)
    {
        var separator = rawHeader.LastIndexOf(' ');
        return separator >= 0 && separator < rawHeader.Length - 1 ? rawHeader[separator + 1] : '?';
    }

    private static FileChangeStatus MapStatus(char status) => status switch
    {
        'M' => FileChangeStatus.Modified,
        'A' => FileChangeStatus.Added,
        'D' => FileChangeStatus.Deleted,
        'R' => FileChangeStatus.Renamed,
        'C' => FileChangeStatus.Copied,
        'T' => FileChangeStatus.Modified, // type change (file <-> symlink)
        'U' => FileChangeStatus.Unmerged,
        _ => FileChangeStatus.Unknown
    };

    private readonly record struct Entry(string Path, string? OriginalPath, FileChangeStatus Status);

    private readonly record struct Stat(int Additions, int Deletions, bool IsBinary);
}
