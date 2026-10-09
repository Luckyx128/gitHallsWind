using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class BranchParser
{
    private const string RemotesPrefix = "remotes/";

    public IReadOnlyList<Branch> Parse(string branchOutput)
    {
        if (string.IsNullOrWhiteSpace(branchOutput)) return Array.Empty<Branch>();

        var branches = new List<Branch>();
        var lines = branchOutput.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.Contains('|'))
            {
                var parts = line.Split('|');
                if (parts.Length < 3) continue;

                var refName = parts[0].Trim();
                var shortName = parts[1].Trim();
                var headIndicator = parts[2].Trim();
                var upstream = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                var track = parts.Length > 4 ? parts[4].Trim() : string.Empty;

                // "refs/remotes/origin/HEAD" is a symbolic ref, not a checkoutable branch.
                if (refName.StartsWith("refs/remotes/", StringComparison.OrdinalIgnoreCase) &&
                    refName.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (shortName.Length == 0) continue;

                bool isCurrent = headIndicator == "*";
                bool isRemote = refName.StartsWith("refs/remotes/", StringComparison.OrdinalIgnoreCase);
                string? remoteName = null;

                if (isRemote)
                {
                    var separator = shortName.IndexOf('/');
                    remoteName = separator > 0 ? shortName.Substring(0, separator) : null;
                }

                bool hasUpstream = !string.IsNullOrEmpty(upstream);
                int ahead = 0;
                int behind = 0;

                if (!string.IsNullOrEmpty(track))
                {
                    var pieces = track.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    foreach (var piece in pieces)
                    {
                        if (piece.StartsWith("ahead ", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(piece.Substring("ahead ".Length).Trim(), out var a))
                            {
                                ahead = a;
                            }
                        }
                        else if (piece.StartsWith("behind ", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(piece.Substring("behind ".Length).Trim(), out var b))
                            {
                                behind = b;
                            }
                        }
                    }
                }

                branches.Add(new Branch(shortName, isCurrent, isRemote, remoteName, hasUpstream, ahead, behind));
                continue;
            }

            bool legacyCurrent = line.StartsWith("*");
            string legacyName = line.Substring(1).Trim(); // Remove '*' or leading space

            if (legacyName.Length == 0) continue;

            // "remotes/origin/HEAD -> origin/main" is a symbolic ref, not a branch
            // you can check out. Listing it produces a phantom entry named
            // "HEAD -> origin/main" in the branch picker.
            if (legacyName.Contains(" -> ")) continue;

            if (legacyName.StartsWith("("))
            {
                // Detached head state, e.g. "(HEAD detached at 1a2b3c4)"
                branches.Add(new Branch(legacyName, legacyCurrent));
                continue;
            }

            bool legacyRemote = legacyName.StartsWith(RemotesPrefix);
            string? legacyRemoteName = null;

            if (legacyRemote)
            {
                // Strip only the "remotes/" prefix: "remotes/origin/main" has to
                // stay "origin/main", otherwise it collides with the local "main".
                legacyName = legacyName.Substring(RemotesPrefix.Length);
                var separator = legacyName.IndexOf('/');
                legacyRemoteName = separator > 0 ? legacyName.Substring(0, separator) : null;
            }

            branches.Add(new Branch(legacyName, legacyCurrent, legacyRemote, legacyRemoteName));
        }

        return branches;
    }
}
