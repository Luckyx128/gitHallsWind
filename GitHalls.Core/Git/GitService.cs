using GitHalls.Core.Git.Parsers;
using GitHalls.Core.Models;

namespace GitHalls.Core.Git;

public class GitService
{
    /// <summary>Bytes inspected when deciding whether an untracked file is binary.</summary>
    private const int BinarySniffLength = 8192;

    private readonly IGitProcessRunner _runner;
    private readonly StatusParser _statusParser;
    private readonly DiffParser _diffParser;
    private readonly CommitLogParser _logParser;
    private readonly BranchParser _branchParser;

    public GitService(IGitProcessRunner? runner = null)
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

    /// <summary>
    /// Diff of <paramref name="change"/> against HEAD. Deliberately not split
    /// into staged/unstaged: a partially staged file would then show only half
    /// of what actually changed on disk.
    /// </summary>
    public async Task<FileDiff> GetDiffAsync(string repoPath, FileChange change, CancellationToken cancellationToken = default)
    {
        if (change.IndexStatus == FileChangeStatus.Untracked)
        {
            // git produces no diff for a file it doesn't track yet, so the
            // whole content is synthesized as additions.
            var fullPath = Path.Combine(repoPath, change.Path);
            if (!File.Exists(fullPath)) return new FileDiff(change.Path, Array.Empty<DiffLine>());

            if (await IsBinaryFileAsync(fullPath, cancellationToken))
            {
                return new FileDiff(change.Path, new[]
                {
                    new DiffLine(DiffParser.BinaryFileText, DiffLineType.HunkHeader, null, null)
                }, isBinary: true);
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            return _diffParser.SyntheticAllAdditions(change.Path, content);
        }

        var result = await _runner.RunAsync(
            repoPath,
            new[] { "diff", "--no-color", "--unified=3", "HEAD", "--", change.Path },
            cancellationToken: cancellationToken);

        return _diffParser.Parse(change.Path, result.StandardOutput);
    }

    /// <summary>
    /// True when the first <see cref="BinarySniffLength"/> bytes contain a NUL —
    /// the same cheap heuristic git itself uses.
    /// </summary>
    private static async Task<bool> IsBinaryFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        var buffer = new byte[BinarySniffLength];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    public async Task<IReadOnlyList<Commit>> GetLogAsync(string repoPath, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var format = "%H%n%an%n%ae%n%aI%n%B%n---COMMIT_END---";
        var args = new[] { "log", $"-n {maxCount}", $"--pretty=format:{format}" };

        var result = await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        return _logParser.Parse(result.StandardOutput);
    }

    /// <summary>
    /// Paths a commit touched. The empty --pretty=format: suppresses the commit
    /// header so only the file list comes back.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetCommitChangedPathsAsync(string repoPath, string hash, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            repoPath,
            new[] { "show", "--pretty=format:", "--name-only", hash },
            cancellationToken: cancellationToken);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>Diff a single commit introduced for one path.</summary>
    public async Task<FileDiff> GetCommitFileDiffAsync(string repoPath, string hash, string filePath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            repoPath,
            new[] { "show", "--no-color", "--pretty=format:", hash, "--", filePath },
            cancellationToken: cancellationToken);

        return _diffParser.Parse(filePath, result.StandardOutput);
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

    /// <summary>
    /// Stages an explicit list of paths. Not the same as "add ." — that also
    /// picks up whatever appeared on disk since the list was rendered.
    /// </summary>
    public async Task StageAsync(string repoPath, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0) return;
        await _runner.RunAsync(repoPath, new[] { "add", "--" }.Concat(paths), cancellationToken: cancellationToken);
    }

    public async Task UnstageAsync(string repoPath, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0) return;
        await _runner.RunAsync(repoPath, new[] { "restore", "--staged", "--" }.Concat(paths), cancellationToken: cancellationToken);
    }

    public async Task CommitAsync(string repoPath, string message, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "commit", "-m", message }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Undoes a change. Which git commands that takes depends entirely on the
    /// file's status: "restore" alone only ever handles a modified file.
    /// </summary>
    public async Task DiscardAsync(string repoPath, FileChange change, CancellationToken cancellationToken = default)
    {
        var path = change.Path;

        switch (change.IndexStatus)
        {
            case FileChangeStatus.Untracked:
                await _runner.RunAsync(repoPath, new[] { "clean", "-f", "--", path }, cancellationToken: cancellationToken);
                break;

            case FileChangeStatus.Added:
                await _runner.RunAsync(repoPath, new[] { "reset", "HEAD", "--", path }, cancellationToken: cancellationToken);
                await _runner.RunAsync(repoPath, new[] { "clean", "-f", "--", path }, cancellationToken: cancellationToken);
                break;

            case FileChangeStatus.Renamed:
            case FileChangeStatus.Copied:
                await _runner.RunAsync(repoPath, new[] { "reset", "HEAD", "--", path }, cancellationToken: cancellationToken);
                await _runner.RunAsync(repoPath, new[] { "clean", "-f", "--", path }, cancellationToken: cancellationToken);
                if (!string.IsNullOrEmpty(change.OriginalPath))
                {
                    // The rename left the original missing from the worktree —
                    // bring it back, otherwise "discard" silently deletes a file.
                    await _runner.RunAsync(repoPath, new[] { "checkout", "HEAD", "--", change.OriginalPath }, cancellationToken: cancellationToken);
                }
                break;

            default:
                await _runner.RunAsync(repoPath, new[] { "checkout", "HEAD", "--", path }, cancellationToken: cancellationToken);
                break;
        }
    }

    public async Task CheckoutAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "checkout", branchName }, cancellationToken: cancellationToken);
    }

    public async Task FetchAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "fetch" }, cancellationToken: cancellationToken);
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

    /// <summary>
    /// Clones into <paramref name="parentDirectory"/>/&lt;repository name&gt; and
    /// returns that path, so the caller opens the directory git actually created.
    /// </summary>
    public async Task<string> CloneAsync(string parentDirectory, string remoteUrl, CancellationToken cancellationToken = default)
    {
        var name = RepositoryNameFromCloneUrl(remoteUrl);
        var destination = Path.Combine(parentDirectory, name);

        Directory.CreateDirectory(parentDirectory);
        await _runner.RunAsync(parentDirectory, new[] { "clone", remoteUrl, destination }, cancellationToken: cancellationToken);

        return destination;
    }

    /// <summary>
    /// Directory name git would pick for a clone URL. Handles trailing slashes,
    /// the ".git" suffix, and the "git@host:owner/repo.git" scp-like form,
    /// whose last separator is ':' rather than '/'.
    /// </summary>
    public static string RepositoryNameFromCloneUrl(string remoteUrl)
    {
        var name = (remoteUrl ?? string.Empty).Trim();
        name = name.TrimEnd('/', '\\');

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 4);
        }

        var separator = name.LastIndexOfAny(new[] { '/', '\\', ':' });
        if (separator >= 0 && separator < name.Length - 1)
        {
            name = name.Substring(separator + 1);
        }

        return name;
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
