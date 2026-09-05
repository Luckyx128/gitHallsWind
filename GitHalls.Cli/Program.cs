using GitHalls.Core.Diff;
using GitHalls.Core.Git;

namespace GitHalls.Cli;

/// <summary>
/// Console harness for the git layer. Parsing edge cases are far cheaper to
/// catch here, against a real repository, than through the UI.
/// </summary>
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
                var language = SyntaxLanguage.ForPath(change.Path) ?? "-";
                Console.WriteLine($"[{change.IndexStatus},{change.WorkTreeStatus}] {change.Path}{orig} <{language}>");
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
                var remote = branch.IsRemote ? $" [remote: {branch.RemoteName}]" : "";
                Console.WriteLine($"{mark} {branch.Name}{remote}");
            }
            Console.WriteLine();

            if (status.Count > 0)
            {
                Console.WriteLine("=== DIFF (First file) ===");
                var firstChange = status[0];
                var diff = await gitService.GetDiffAsync(repoPath, firstChange);

                Console.WriteLine($"Diff for {diff.FilePath} ({diff.Lines.Count} lines, +{diff.Additions} -{diff.Deletions}):");
                foreach (var line in diff.Lines.Take(15))
                {
                    var old = line.OldLineNumber?.ToString() ?? "";
                    var @new = line.NewLineNumber?.ToString() ?? "";
                    Console.WriteLine($"{old,5} {@new,5} [{line.Type}] {line.Content}");
                }
                if (diff.Lines.Count > 15) Console.WriteLine("...");
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
