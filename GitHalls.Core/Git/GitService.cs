using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;

namespace GitHalls.Core.Git;

public class GitService
{
    private readonly IGitProcessRunner _runner;
    private readonly StatusParser _statusParser;
    private readonly DiffParser _diffParser;
    private readonly CommitLogParser _logParser;
    private readonly BranchParser _branchParser;

    public GitService(IGitProcessRunner runner = null)
    {
        _runner = runner ?? new GitProcessRunner();
        _statusParser = new StatusParser();
        _diffParser = new DiffParser();
        _logParser = new CommitLogParser();
        _branchParser = new BranchParser();
    }

    public async Task<IReadOnlyList<FileChange>> GetStatusAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(repoPath, new[] { "status", "--porcelain=v1", "-z", "-uall" }, cancellationToken: cancellationToken);
        return _statusParser.Parse(result.StandardOutput);
    }

    public async Task<FileDiff> GetDiffAsync(string repoPath, string filePath, bool staged, bool isUntracked = false, CancellationToken cancellationToken = default)
    {
        if (isUntracked)
        {
            var fullPath = Path.Combine(repoPath, filePath);
            if (!File.Exists(fullPath)) return new FileDiff(filePath, Array.Empty<DiffLine>());

            var lines = new List<DiffLine>
            {
                new DiffLine($"diff --git a/{filePath} b/{filePath}", DiffLineType.Header, null, null),
                new DiffLine($"--- /dev/null", DiffLineType.Header, null, null),
                new DiffLine($"+++ b/{filePath}", DiffLineType.Header, null, null)
            };

            var contentLines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
            lines.Add(new DiffLine($"@@ -0,0 +1,{contentLines.Length} @@", DiffLineType.Header, null, null));

            for (int i = 0; i < contentLines.Length; i++)
            {
                lines.Add(new DiffLine($"+{contentLines[i]}", DiffLineType.Addition, null, i + 1));
            }

            return new FileDiff(filePath, lines);
        }

        var args = new List<string> { "diff", "--unified=3" };
        if (staged) args.Add("--cached");
        args.Add("--");
        args.Add(filePath);

        var result = await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        return _diffParser.Parse(filePath, result.StandardOutput);
    }

    public async Task<IReadOnlyList<Commit>> GetLogAsync(string repoPath, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var format = "%H%n%an%n%ae%n%aI%n%B%n---COMMIT_END---";
        var args = new[] { "log", $"-n {maxCount}", $"--pretty=format:{format}" };
        
        var result = await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        return _logParser.Parse(result.StandardOutput);
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(repoPath, new[] { "branch", "-a", "--no-color" }, cancellationToken: cancellationToken);
        return _branchParser.Parse(result.StandardOutput);
    }

    public async Task StageAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "add", "--", filePath }, cancellationToken: cancellationToken);
    }

    public async Task UnstageAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "restore", "--staged", "--", filePath }, cancellationToken: cancellationToken);
    }

    public async Task CommitAsync(string repoPath, string message, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "commit", "-m", message }, cancellationToken: cancellationToken);
    }

    public async Task DiscardAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "restore", "--", filePath }, cancellationToken: cancellationToken);
    }

    public async Task CheckoutAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "checkout", branchName }, cancellationToken: cancellationToken);
    }

    public async Task PushAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "push" }, cancellationToken: cancellationToken);
    }

    public async Task PushPublishAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "push", "-u", "origin", "HEAD" }, cancellationToken: cancellationToken);
    }

    public async Task PullAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "pull" }, cancellationToken: cancellationToken);
    }

    public async Task CloneAsync(string targetDirectory, string remoteUrl, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(targetDirectory, new[] { "clone", remoteUrl, "." }, cancellationToken: cancellationToken);
    }

    public async Task<bool> HasUpstreamAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner.RunAsync(repoPath, new[] { "rev-parse", "--abbrev-ref", branchName + "@{u}" }, cancellationToken: cancellationToken);
            return !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch
        {
            return false;
        }
    }

    public async Task MergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "merge", branchName }, cancellationToken: cancellationToken);
    }
}
