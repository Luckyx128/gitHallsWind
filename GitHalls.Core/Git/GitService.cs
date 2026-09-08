using GitHalls.Core.Commits;
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
    private readonly CommitFileParser _commitFileParser;

    public GitService(IGitProcessRunner? runner = null)
    {
        _runner = runner ?? new GitProcessRunner();
        _statusParser = new StatusParser();
        _diffParser = new DiffParser();
        _logParser = new CommitLogParser();
        _branchParser = new BranchParser();
        _commitFileParser = new CommitFileParser();
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

    /// <summary>
    /// Staged line counts per path, from <c>git diff --cached --numstat</c>.
    /// Feeds the conventional-commit suggestion.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ConventionalCommitSuggester.LineStats>> GetStagedNumstatAsync(
        string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(repoPath, new[] { "diff", "--cached", "--numstat" }, cancellationToken: cancellationToken);

        var stats = new Dictionary<string, ConventionalCommitSuggester.LineStats>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            // A binary file reports "-\t-\tpath" — no line counts, so skip it.
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[0], out var additions)) continue;
            if (!int.TryParse(parts[1], out var deletions)) continue;

            stats[parts[2].Trim()] = new ConventionalCommitSuggester.LineStats(additions, deletions);
        }

        return stats;
    }

    /// <summary>
    /// How far the current branch is ahead of and behind its upstream, or null
    /// when it has no upstream (a new branch that was never pushed).
    ///
    /// Reads the "# branch.ab +N -M" header of the v2 porcelain format, which
    /// gives both counts in the same call that already reports the status.
    /// </summary>
    public async Task<(int Ahead, int Behind)?> GetBranchSyncAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(repoPath, new[] { "status", "--porcelain=v2", "--branch" }, cancellationToken: cancellationToken);

        const string marker = "# branch.ab ";
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            if (!line.StartsWith(marker, StringComparison.Ordinal)) continue;

            var parts = line.Substring(marker.Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var ahead)) continue;
            if (!int.TryParse(parts[1], out var behind)) continue;

            // git reports behind as a negative number.
            return (ahead, Math.Abs(behind));
        }

        return null;
    }

    /// <summary>What <see cref="CommitLogParser"/> reads back — the two must change together.</summary>
    private const string LogFormat = "%H%n%an%n%ae%n%aI%n%B%n---COMMIT_END---";

    public async Task<IReadOnlyList<Commit>> GetLogAsync(string repoPath, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var args = new[] { "log", $"-n {maxCount}", $"--pretty=format:{LogFormat}" };

        var result = await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        return _logParser.Parse(result.StandardOutput);
    }

    /// <summary>
    /// Commits HEAD has and <paramref name="baseRef"/> does not, newest first —
    /// exactly what a pull request would carry.
    ///
    /// An unknown ref is a normal answer here (a base branch this clone has
    /// never fetched), not a failure: nothing to compare means nothing ahead.
    /// </summary>
    public async Task<IReadOnlyList<Commit>> GetCommitsAheadAsync(string repoPath, string baseRef, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner.RunAsync(
                repoPath,
                new[] { "log", $"-n {maxCount}", $"--pretty=format:{LogFormat}", $"{baseRef}..HEAD" },
                cancellationToken: cancellationToken);

            return _logParser.Parse(result.StandardOutput);
        }
        catch (GitException)
        {
            return Array.Empty<Commit>();
        }
    }

    /// <summary>
    /// The branch a pull request goes into when the user picks none:
    /// "origin/HEAD", which a clone points at the remote's default branch.
    /// Null when it was never set — an old clone, or a repository with no remote.
    /// </summary>
    public async Task<string?> GetDefaultBaseRefAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner.RunAsync(
                repoPath,
                new[] { "symbolic-ref", "--short", "refs/remotes/origin/HEAD" },
                cancellationToken: cancellationToken);

            var value = result.StandardOutput.Trim();
            return value.Length == 0 ? null : value;
        }
        catch (GitException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every file a commit touched, with its status and line counts but without
    /// any diff content. One process for the whole commit — the diff of a single
    /// file comes later, from <see cref="GetCommitFileDiffAsync"/>, and only for
    /// the file the user opens.
    ///
    /// The empty --pretty=format: suppresses the commit header, so only the file
    /// list comes back.
    /// </summary>
    public async Task<IReadOnlyList<CommitFile>> GetCommitFilesAsync(string repoPath, string hash, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            repoPath,
            new[] { "show", "--pretty=format:", "--raw", "--numstat", "-z", hash },
            cancellationToken: cancellationToken);

        return _commitFileParser.Parse(result.StandardOutput);
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

    // MARK: - Identity

    /// <summary>
    /// The author git would record for a commit here, or null when neither name
    /// nor email is configured anywhere. Reads the effective value, so it may
    /// come from the repository, the global file or the system one.
    /// </summary>
    public async Task<GitAuthor?> GetAuthorAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var name = await TryReadConfigAsync(repoPath, "user.name", localOnly: false, cancellationToken);
        var email = await TryReadConfigAsync(repoPath, "user.email", localOnly: false, cancellationToken);

        if (name == null && email == null) return null;

        return new GitAuthor(name ?? string.Empty, email ?? string.Empty);
    }

    /// <summary>
    /// True when this repository sets its own identity, rather than inheriting
    /// the global one. Worth saying out loud in the UI: the difference decides
    /// whether a change here affects every other repository on the machine.
    /// </summary>
    public async Task<bool> HasLocalIdentityAsync(string repoPath, CancellationToken cancellationToken = default) =>
        await TryReadConfigAsync(repoPath, "user.name", localOnly: true, cancellationToken) != null;

    /// <summary>Writes the identity into this repository only.</summary>
    public async Task SetIdentityAsync(string repoPath, string name, string email, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "config", "--local", "user.name", name }, cancellationToken: cancellationToken);
        await _runner.RunAsync(repoPath, new[] { "config", "--local", "user.email", email }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// "git config <key>" exits non-zero when the key is unset, which the runner
    /// raises as an error. An unset identity is a normal state, not a failure.
    /// </summary>
    private async Task<string?> TryReadConfigAsync(string repoPath, string key, bool localOnly, CancellationToken cancellationToken)
    {
        var args = localOnly ? new[] { "config", "--local", key } : new[] { "config", key };

        try
        {
            var result = await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
            var value = result.StandardOutput.Trim();
            return value.Length == 0 ? null : value;
        }
        catch (GitException)
        {
            return null;
        }
    }

    public async Task<string?> GetRemoteUrlAsync(string repoPath, string remoteName = "origin", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runner.RunAsync(repoPath, new[] { "remote", "get-url", remoteName }, cancellationToken: cancellationToken);
            var url = result.StandardOutput.Trim();
            return url.Length == 0 ? null : url;
        }
        catch (GitException)
        {
            // No such remote — the repository simply has none.
            return null;
        }
    }

    /// <summary>
    /// Puts a username into the https remote, so a push authenticates as that
    /// account instead of whichever one the credential helper answers with
    /// first. Only https carries a username; an SSH remote picks its account
    /// through the key, and rewriting it here would be wrong.
    /// </summary>
    public async Task SetRemoteUsernameAsync(string repoPath, string username, string remoteName = "origin", CancellationToken cancellationToken = default)
    {
        var current = await GetRemoteUrlAsync(repoPath, remoteName, cancellationToken)
            ?? throw new GitException($"Remote \"{remoteName}\" has no URL.", string.Empty);

        if (!current.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new GitException("Only an https remote carries a username; this one does not.", current);
        }

        if (GitRemoteUrl.ParseRemote(current) is not { } parts)
        {
            throw new GitException("That remote is not a recognized Git URL this app can rewrite.", current);
        }

        var updated = GitRemoteUrl.WithUsername(parts.Host, parts.Path, username);
        await _runner.RunAsync(repoPath, new[] { "remote", "set-url", remoteName, updated }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Hands a username and token to git's own credential helper — on Windows,
    /// the Credential Manager that Git for Windows installs by default. The app
    /// keeps no copy: it writes the credential and forgets it.
    /// </summary>
    public async Task ApproveCredentialAsync(string username, string token, string host = "github.com", CancellationToken cancellationToken = default)
    {
        // The trailing blank line ends the input; git waits for it.
        var input = $"protocol=https\nhost={host}\nusername={username}\npassword={token}\n\n";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        await _runner.RunAsync(home, new[] { "credential", "approve" }, stdinData: input, cancellationToken: cancellationToken);
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

    /// <summary>
    /// Commits with a summary and an optional description, passed as two -m
    /// arguments so git formats the blank line between them itself.
    /// </summary>
    public async Task CommitAsync(string repoPath, string summary, string? description = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "commit", "-m", summary };
        if (!string.IsNullOrWhiteSpace(description))
        {
            args.Add("-m");
            args.Add(description);
        }

        await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
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

    /// <summary>Creates a branch from the current HEAD and switches to it.</summary>
    public async Task CreateBranchAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "checkout", "-b", branchName }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The bytes of a path at a revision — "HEAD", a hash, or "&lt;hash&gt;^".
    ///
    /// Null when the path is not there: a file that was just added has no
    /// previous revision, and a deleted one has no current one. git says so with
    /// a non-zero exit, which is a normal answer here and not a failure.
    /// </summary>
    public async Task<byte[]?> GetBlobAsync(string repoPath, string revision, string filePath, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunBytesAsync(repoPath, new[] { "show", $"{revision}:{filePath}" }, cancellationToken);

        return result.ExitCode == 0 ? result.Output : null;
    }

    /// <summary>
    /// The bytes on disk — the working tree side of a change, which no revision
    /// names. Null when the file is gone, which a deletion makes normal.
    /// </summary>
    public static async Task<byte[]?> GetWorkingTreeBytesAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(repoPath, filePath.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            return File.Exists(full) ? await File.ReadAllBytesAsync(full, cancellationToken) : null;
        }
        catch (IOException)
        {
            return null;
        }
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
    /// Pull on a branch that is also ahead. Since git 2.27 a plain "git pull"
    /// aborts on divergent branches when the reconciliation is not configured
    /// ("Need to specify how to reconcile divergent branches"), so a merge is
    /// asked for explicitly — but only when the user set no preference of their
    /// own, which is theirs to keep.
    /// </summary>
    public async Task PullDivergentAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var configured = await TryReadConfigAsync(repoPath, "pull.rebase", localOnly: false, cancellationToken)
            ?? await TryReadConfigAsync(repoPath, "pull.ff", localOnly: false, cancellationToken);

        var args = configured == null
            ? new[] { "pull", "--no-rebase" }
            : new[] { "pull" };

        await _runner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
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

    public async Task MergeAsync(string repoPath, string branchName, CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync(repoPath, new[] { "merge", branchName }, cancellationToken: cancellationToken);
    }
}
