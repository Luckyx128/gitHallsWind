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

            bool isCurrent = line.StartsWith("*");
            string name = line.Substring(1).Trim(); // Remove '*' or leading space

            if (name.Length == 0) continue;

            // "remotes/origin/HEAD -> origin/main" is a symbolic ref, not a branch
            // you can check out. Listing it produces a phantom entry named
            // "HEAD -> origin/main" in the branch picker.
            if (name.Contains(" -> ")) continue;

            if (name.StartsWith("("))
            {
                // Detached head state, e.g. "(HEAD detached at 1a2b3c4)"
                branches.Add(new Branch(name, isCurrent));
                continue;
            }

            bool isRemote = name.StartsWith(RemotesPrefix);
            string? remoteName = null;

            if (isRemote)
            {
                // Strip only the "remotes/" prefix: "remotes/origin/main" has to
                // stay "origin/main", otherwise it collides with the local "main".
                name = name.Substring(RemotesPrefix.Length);
                var separator = name.IndexOf('/');
                remoteName = separator > 0 ? name.Substring(0, separator) : null;
            }

            branches.Add(new Branch(name, isCurrent, isRemote, remoteName));
        }

        return branches;
    }
}
