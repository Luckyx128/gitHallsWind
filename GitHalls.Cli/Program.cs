using GitHalls.Core.Git;

namespace GitHalls.Cli;

class Program
{
    static async Task Main(string[] args)
    {
        var repoPath = args.Length > 0 ? args[0] : Environment.CurrentDirectory;

        Console.WriteLine($"Running GitHalls CLI validation against repo: {repoPath}\n");

        var gitService = new GitService();

        try
        {
            Console.WriteLine("=== STATUS ===");
            var status = await gitService.GetStatusAsync(repoPath);
            foreach (var change in status)
            {
                var orig = change.OriginalPath != null ? $" (was {change.OriginalPath})" : "";
                Console.WriteLine($"[{change.IndexStatus},{change.WorkTreeStatus}] {change.Path}{orig}");
            }
            Console.WriteLine();

            Console.WriteLine("=== LOG (Top 3) ===");
            var log = await gitService.GetLogAsync(repoPath, 3);
            foreach (var commit in log)
            {
                Console.WriteLine($"{commit.ShortHash} | {commit.AuthorName} | {commit.Message.Split('\n')[0]}");
            }
            Console.WriteLine();

            Console.WriteLine("=== BRANCHES ===");
            var branches = await gitService.GetBranchesAsync(repoPath);
            foreach (var branch in branches)
            {
                var mark = branch.IsCurrent ? "*" : " ";
                Console.WriteLine($"{mark} {branch.Name} {(branch.IsRemote ? $"[Remote: {branch.RemoteName}]" : "")}");
            }
            Console.WriteLine();

            if (status.Count > 0)
            {
                Console.WriteLine("=== DIFF (First file) ===");
                var firstChange = status.First();
                var diff = await gitService.GetDiffAsync(repoPath, firstChange.Path, firstChange.IsStaged);
                
                if (diff.IsBinary)
                {
                    Console.WriteLine($"{firstChange.Path} is a binary file.");
                }
                else
                {
                    Console.WriteLine($"Diff for {firstChange.Path} ({diff.Lines.Count} lines):");
                    foreach (var line in diff.Lines.Take(15))
                    {
                        Console.WriteLine($"[{line.Type}] {line.Content}");
                    }
                    if (diff.Lines.Count > 15) Console.WriteLine("...");
                }
            }
            else
            {
                Console.WriteLine("No changes to diff.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
