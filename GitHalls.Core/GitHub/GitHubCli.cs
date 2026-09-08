namespace GitHalls.Core.GitHub;

using System.Diagnostics;
using System.Text;
using GitHalls.Core.Models;

/// <summary>
/// Wrapper for the `gh` CLI. Same shape as <see cref="Git.GitProcessRunner"/>:
/// one process per action, pipes drained as a whole, no shell.
/// Port of GitHubService.swift.
/// </summary>
public class GitHubCli
{
    /// <summary>
    /// Is `gh` installed and on PATH? Asked before every use, and answered
    /// without throwing: a missing CLI is a normal state that falls back to the
    /// browser, not a failure to report.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunAsync(Path.GetTempPath(), new[] { "--version" }, cancellationToken);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the pull request and returns its URL — with --title and --body
    /// given, `gh pr create` needs no interactive terminal and prints the URL
    /// on stdout.
    /// </summary>
    public async Task<string> CreatePullRequestAsync(
        string repoPath, string title, string body, string? baseBranch, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "pr", "create", "--title", title, "--body", body };
        if (!string.IsNullOrWhiteSpace(baseBranch))
        {
            arguments.Add("--base");
            arguments.Add(baseBranch);
        }

        var result = await RunAsync(repoPath, arguments, cancellationToken);
        if (result.ExitCode != 0) throw GitException.Parse(result.StandardError);

        return result.StandardOutput.Trim();
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(
        string workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        // gh pages its output through a pager and colours it when it believes a
        // terminal is watching; neither survives being read from a pipe.
        startInfo.Environment["GH_PAGER"] = string.Empty;
        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["NO_COLOR"] = "1";

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new GitException("GitHub CLI (gh) is not installed or not on PATH.", ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, (await stdoutTask).Trim(), (await stderrTask).Trim());
    }
}
