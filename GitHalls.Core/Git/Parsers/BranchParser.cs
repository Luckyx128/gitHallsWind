using GitHalls.Core.Models;

namespace GitHalls.Core.Git.Parsers;

public class BranchParser
{
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

            if (name.StartsWith("(HEAD detached at"))
            {
                // Detached head state
                branches.Add(new Branch(name, isCurrent));
                continue;
            }

            bool isRemote = name.StartsWith("remotes/");
            string remoteName = null;

            if (isRemote)
            {
                // e.g. remotes/origin/main
                var parts = name.Split('/');
                if (parts.Length >= 3)
                {
                    remoteName = parts[1];
                    name = string.Join("/", parts.Skip(2));
                }
            }

            branches.Add(new Branch(name, isCurrent, isRemote, remoteName));
        }

        return branches;
    }
}
