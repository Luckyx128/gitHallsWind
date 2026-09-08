using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.App.Services;
using GitHalls.Core.Commits;
using GitHalls.Core.Git;
using GitHalls.Core.Models;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace GitHalls.App.ViewModels;

public partial class RepositoryViewModel : ObservableObject, IDisposable
{
    /// <summary>Window in which a burst of .git file events collapses into one refresh.</summary>
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>Fallback poll, for changes no file event reports (e.g. a remote moving).</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary>Shortest gap between two focus-triggered fetches.</summary>
    private static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(2);

    private readonly GitService _gitService;
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherQueue _dispatcher;

    private DispatcherQueueTimer? _debounceTimer;
    private DispatcherQueueTimer? _pollTimer;
    private FileSystemWatcher? _gitWatcher;

    /// <summary>A refresh arrived while one was already running — run once more when it finishes.</summary>
    private bool _refreshPending;
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void ClearError() => ErrorMessage = null;

    public ObservableCollection<FileChange> Changes { get; } = new();
    public ObservableCollection<Commit> Commits { get; } = new();
    public ObservableCollection<Branch> Branches { get; } = new();
    public ObservableCollection<string> RecentRepositories { get; } = new();

    /// <summary>Branches recently checked out in the current repository, most recent first.</summary>
    public ObservableCollection<string> RecentBranchNames { get; } = new();

    [ObservableProperty]
    private FileChange? _selectedChange;

    [ObservableProperty]
    private FileDiff? _currentDiff;

    [ObservableProperty]
    private bool _isLoadingDiff;

    [ObservableProperty]
    private Commit? _selectedCommit;

    /// <summary>
    /// The files of the selected commit, without their diffs. Selecting a commit
    /// costs one git call for this list; the diff of a file is loaded when that
    /// file is selected, and no sooner.
    /// </summary>
    public ObservableCollection<CommitFile> CommitFiles { get; } = new();

    [ObservableProperty]
    private CommitFile? _selectedCommitFile;

    /// <summary>Diff of <see cref="SelectedCommitFile"/>, loaded on demand.</summary>
    [ObservableProperty]
    private FileDiff? _commitFileDiff;

    [ObservableProperty]
    private bool _isLoadingCommitFiles;

    [ObservableProperty]
    private bool _isLoadingCommitFileDiff;

    /// <summary>Show diffs side by side rather than unified. Persisted.</summary>
    [ObservableProperty]
    private bool _isSideBySideDiff;

    /// <summary>Who git would record as the author of a commit here.</summary>
    [ObservableProperty]
    private GitAuthor? _currentAuthor;

    /// <summary>
    /// True when this repository sets its own user.name — worth showing, because
    /// it decides whether the identity in use came from here or from the global
    /// config every other repository shares.
    /// </summary>
    [ObservableProperty]
    private bool _hasLocalIdentityOverride;

    [ObservableProperty]
    private bool _isSwitchingIdentity;

    /// <summary>Identity profiles the user saved, in the order they were added.</summary>
    public ObservableCollection<GitIdentity> SavedIdentities { get; } = new();

    /// <summary>
    /// Identifies the most recent async load of each kind. A slow response for
    /// a selection the user has already moved away from must not overwrite what
    /// is on screen, so every load checks its token is still current before
    /// publishing anything.
    /// </summary>
    private Guid _diffRequestToken;
    private Guid _commitFilesRequestToken;
    private Guid _commitFileDiffRequestToken;

    [ObservableProperty]
    private Branch? _currentBranch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitSummary = string.Empty;

    [ObservableProperty]
    private string _commitDescription = string.Empty;

    [ObservableProperty]
    private ConventionalCommitType? _commitType;

    [ObservableProperty]
    private string _commitScope = string.Empty;

    /// <summary>Ahead/behind the upstream. Both zero and <see cref="HasUpstream"/> true means up to date.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSyncAction), nameof(SyncTitle), nameof(SyncGlyph), nameof(CanSync))]
    private int _syncAhead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSyncAction), nameof(SyncTitle), nameof(SyncGlyph), nameof(CanSync))]
    private int _syncBehind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSyncAction), nameof(SyncTitle), nameof(SyncGlyph), nameof(CanSync))]
    private bool _hasUpstream;

    /// <summary>What the sync button does in the current state.</summary>
    public SyncAction CurrentSyncAction => BranchSync.ActionFor(HasUpstream, SyncAhead, SyncBehind);

    /// <summary>Label of the single sync button. Port of SyncButton.swift.</summary>
    public string SyncTitle => CurrentSyncAction switch
    {
        SyncAction.Publish => "Publish Branch",
        SyncAction.Push => $"Push ({SyncAhead})",
        SyncAction.Pull => $"Pull ({SyncBehind})",
        SyncAction.PullThenPush => $"Sync (\u2193{SyncBehind} \u2191{SyncAhead})",
        _ => "Up to date",
    };

    public string SyncGlyph => CurrentSyncAction switch
    {
        SyncAction.Publish or SyncAction.Push => "\uE898",  // Upload
        SyncAction.Pull => "\uE896",                        // Download
        SyncAction.PullThenPush => "\uE895",                // Sync
        _ => "\uE73E",                                      // Checkmark
    };

    /// <summary>Up to date is a state, not an action — the button says so and stays disabled.</summary>
    public bool CanSync => !IsBusy && !string.IsNullOrEmpty(RepositoryPath)
        && CurrentSyncAction != SyncAction.UpToDate;

    /// <summary>
    /// Tri-state for the "stage all" checkbox: true = everything staged,
    /// false = nothing staged, null = a mix. Mirrors the Swift `allStaged`,
    /// with the indeterminate case the Mac list shows implicitly.
    /// </summary>
    public bool? StagedState
    {
        get
        {
            if (Changes.Count == 0) return false;
            if (Changes.All(c => c.IsStaged)) return true;
            if (Changes.All(c => !c.IsStaged)) return false;
            return null;
        }
    }

    /// <summary>Committing needs both something staged and something to say about it.</summary>
    public bool CanCommit => !IsBusy
        && !string.IsNullOrWhiteSpace(CommitSummary)
        && Changes.Any(c => c.IsStaged);

    /// <summary>Staged files only — what the suggestion and the commit actually act on.</summary>
    public IReadOnlyList<FileChange> StagedChanges => Changes.Where(c => c.IsStaged).ToList();

    public bool HasStagedChanges => Changes.Any(c => c.IsStaged);

    public bool HasSelectedChange => SelectedChange != null;

    public RepositoryViewModel(GitService gitService, SettingsStore settingsStore)
    {
        _gitService = gitService;
        _settingsStore = settingsStore;

        // Captured once, here, on the UI thread: FileSystemWatcher raises its
        // events on a threadpool thread, where GetForCurrentThread() is null.
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("RepositoryViewModel must be constructed on the UI thread.");
    }

    /// <summary>Loads persisted settings and reopens the last repository, if any.</summary>
    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();

        RecentRepositories.Clear();
        foreach (var path in settings.RecentRepositories)
        {
            // Drop repositories that were moved or deleted since last time, and
            // collapse entries that differ only in casing or a trailing slash.
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;

            var normalized = NormalizePath(path);
            if (RecentRepositories.Any(r => SamePath(r, normalized))) continue;

            RecentRepositories.Add(normalized);
        }

        _recentBranchesByRepo = settings.RecentBranches ?? new Dictionary<string, List<string>>();

        IsSideBySideDiff = settings.SideBySideDiff;

        SavedIdentities.Clear();
        foreach (var identity in settings.GitIdentities) SavedIdentities.Add(identity);

        if (!string.IsNullOrEmpty(settings.LastOpenedRepository) && Directory.Exists(settings.LastOpenedRepository))
        {
            RepositoryPath = settings.LastOpenedRepository;
        }
    }

    partial void OnRepositoryPathChanged(string? value)
    {
        StopAutoRefresh();

        SelectedChange = null;
        CurrentDiff = null;
        SelectedCommit = null;
        CurrentAuthor = null;
        HasLocalIdentityOverride = false;
        ErrorMessage = null;

        if (string.IsNullOrEmpty(value)) return;

        var normalized = NormalizePath(value);
        if (!SamePath(normalized, value))
        {
            // Re-enters this handler with the canonical form.
            RepositoryPath = normalized;
            return;
        }

        PromoteRecent(normalized);
        LoadRecentBranches();
        StartAutoRefresh();
        _ = RefreshAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanSync));
        CommitCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChangeChanged(FileChange? value)
    {
        OnPropertyChanged(nameof(HasSelectedChange));

        if (value != null && !string.IsNullOrEmpty(RepositoryPath))
        {
            _ = LoadDiffAsync(value);
        }
        else
        {
            CurrentDiff = null;
        }
    }

    partial void OnIsSideBySideDiffChanged(bool value) => Save();

    partial void OnSelectedCommitChanged(Commit? value)
    {
        _ = LoadCommitFilesAsync();
    }

    partial void OnSelectedCommitFileChanged(CommitFile? value)
    {
        _ = LoadCommitFileDiffAsync(value);
    }

    /// <summary>
    /// Lists what the selected commit touched. One git call, no diff content:
    /// a 40-file commit used to mean 41 calls before anything appeared, and the
    /// pane then rendered ten diffs the user had not asked for.
    /// </summary>
    private async Task LoadCommitFilesAsync()
    {
        var repoPath = RepositoryPath;
        var commit = SelectedCommit;

        var token = Guid.NewGuid();
        _commitFilesRequestToken = token;

        // Whatever was on screen belongs to the commit we just left.
        SelectedCommitFile = null;
        CommitFiles.Clear();

        if (string.IsNullOrEmpty(repoPath) || commit == null)
        {
            IsLoadingCommitFiles = false;
            return;
        }

        IsLoadingCommitFiles = true;

        try
        {
            var files = await _gitService.GetCommitFilesAsync(repoPath, commit.Hash);

            // The user moved on while this was loading.
            if (_commitFilesRequestToken != token) return;

            foreach (var file in files) CommitFiles.Add(file);

            // A single-file commit has nothing to choose, so choose it.
            if (CommitFiles.Count == 1) SelectedCommitFile = CommitFiles[0];
        }
        catch (Exception ex)
        {
            if (_commitFilesRequestToken != token) return;
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (_commitFilesRequestToken == token) IsLoadingCommitFiles = false;
        }
    }

    private async Task LoadCommitFileDiffAsync(CommitFile? file)
    {
        var repoPath = RepositoryPath;
        var commit = SelectedCommit;

        var token = Guid.NewGuid();
        _commitFileDiffRequestToken = token;

        if (file == null || commit == null || string.IsNullOrEmpty(repoPath))
        {
            CommitFileDiff = null;
            IsLoadingCommitFileDiff = false;
            return;
        }

        IsLoadingCommitFileDiff = true;

        try
        {
            var diff = await _gitService.GetCommitFileDiffAsync(repoPath, commit.Hash, file.Path);

            if (_commitFileDiffRequestToken != token) return;
            CommitFileDiff = diff;
        }
        catch (Exception ex)
        {
            if (_commitFileDiffRequestToken != token) return;
            CommitFileDiff = null;
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            if (_commitFileDiffRequestToken == token) IsLoadingCommitFileDiff = false;
        }
    }

    private const int MaxRecentRepositories = 10;
    private const int MaxRecentBranches = 5;

    /// <summary>Recent branches per repository, as loaded from settings.</summary>
    private Dictionary<string, List<string>> _recentBranchesByRepo = new();

    /// <summary>
    /// Canonical form of a repository path, so the same repository is never
    /// listed twice. Windows paths reach us from three places — the folder
    /// picker, a clone, and the settings file — and they disagree about
    /// trailing separators and casing.
    /// </summary>
    public static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            // GetFullPath throws on a malformed path; the raw value still beats
            // dropping the entry entirely.
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void PromoteRecent(string path)
    {
        var normalized = NormalizePath(path);

        for (int i = RecentRepositories.Count - 1; i >= 0; i--)
        {
            if (SamePath(RecentRepositories[i], normalized)) RecentRepositories.RemoveAt(i);
        }

        RecentRepositories.Insert(0, normalized);
        while (RecentRepositories.Count > MaxRecentRepositories)
        {
            RecentRepositories.RemoveAt(RecentRepositories.Count - 1);
        }

        Save();
    }

    /// <summary>Forgets a repository without opening it. Mirrors the Swift forgetRecent.</summary>
    public void ForgetRecent(string path)
    {
        var normalized = NormalizePath(path);
        for (int i = RecentRepositories.Count - 1; i >= 0; i--)
        {
            if (SamePath(RecentRepositories[i], normalized)) RecentRepositories.RemoveAt(i);
        }
        Save();
    }

    /// <summary>
    /// Writes only the parts this view model owns. The settings file has other
    /// writers — the Jira account, for one — and replacing the whole object here
    /// would drop whatever they had just saved.
    /// </summary>
    private void Save() => _ = _settingsStore.UpdateAsync(settings =>
    {
        settings.RecentRepositories = RecentRepositories.ToList();
        settings.LastOpenedRepository = RepositoryPath;
        settings.RecentBranches = _recentBranchesByRepo;
        settings.SideBySideDiff = IsSideBySideDiff;
        settings.GitIdentities = SavedIdentities.ToList();
    });

    private void LoadRecentBranches()
    {
        RecentBranchNames.Clear();
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        if (!_recentBranchesByRepo.TryGetValue(RepositoryPath, out var names)) return;

        foreach (var name in names) RecentBranchNames.Add(name);
    }

    private void PromoteRecentBranch(string branchName)
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath) || string.IsNullOrWhiteSpace(branchName)) return;

        var names = _recentBranchesByRepo.TryGetValue(repoPath, out var existing) ? existing : new List<string>();
        names.RemoveAll(n => string.Equals(n, branchName, StringComparison.Ordinal));
        names.Insert(0, branchName);
        if (names.Count > MaxRecentBranches) names.RemoveRange(MaxRecentBranches, names.Count - MaxRecentBranches);

        _recentBranchesByRepo[repoPath] = names;
        LoadRecentBranches();
        Save();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;

        if (IsBusy)
        {
            // Don't drop this request: the event that triggered it may well be
            // the one carrying the change the user is waiting to see.
            _refreshPending = true;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            do
            {
                _refreshPending = false;
                await RefreshOnceAsync();
            }
            while (_refreshPending);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath)) return;

        var status = await _gitService.GetStatusAsync(repoPath);
        MergeChanges(status);

        if (SelectedChange != null)
        {
            SelectedChange = Changes.FirstOrDefault(c => c.Path == SelectedChange.Path);
        }

        // Merged in place for the same reason as the branch list: clearing it
        // drops the History selection, and the periodic refresh would then close
        // the commit detail the user is reading every minute.
        MergeCommits(await _gitService.GetLogAsync(repoPath));

        var branches = await _gitService.GetBranchesAsync(repoPath);
        MergeBranches(branches);

        CurrentAuthor = await _gitService.GetAuthorAsync(repoPath);
        HasLocalIdentityOverride = await _gitService.HasLocalIdentityAsync(repoPath);

        // One call answers both "is there an upstream" and "how far apart are
        // we", replacing a separate rev-parse per refresh.
        var sync = await _gitService.GetBranchSyncAsync(repoPath);
        HasUpstream = sync != null;
        SyncAhead = sync?.Ahead ?? 0;
        SyncBehind = sync?.Behind ?? 0;
        SyncCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Replaces only what actually changed, so the list doesn't flicker.</summary>
    private void MergeCommits(IReadOnlyList<Commit> commits)
    {
        for (int i = Commits.Count - 1; i >= 0; i--)
        {
            if (!commits.Any(c => c.Hash == Commits[i].Hash)) Commits.RemoveAt(i);
        }

        for (int i = 0; i < commits.Count; i++)
        {
            if (Commits.Any(c => c.Hash == commits[i].Hash)) continue;
            Commits.Insert(Math.Min(i, Commits.Count), commits[i]);
        }

        // The selected commit was rewritten or dropped (amend, rebase, reset).
        if (SelectedCommit != null && !Commits.Any(c => c.Hash == SelectedCommit.Hash))
        {
            SelectedCommit = null;
        }
    }

    private void MergeChanges(IReadOnlyList<FileChange> status)
    {
        for (int i = Changes.Count - 1; i >= 0; i--)
        {
            if (!status.Any(s => s.Path == Changes[i].Path)) Changes.RemoveAt(i);
        }

        foreach (var change in status)
        {
            var existing = Changes.FirstOrDefault(c => c.Path == change.Path);
            if (existing == null)
            {
                Changes.Add(change);
            }
            else if (existing.IndexStatus != change.IndexStatus || existing.WorkTreeStatus != change.WorkTreeStatus)
            {
                Changes[Changes.IndexOf(existing)] = change;
            }
        }

        // All derived from the list, and none is recomputed on its own.
        OnPropertyChanged(nameof(StagedState));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(StagedChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        CommitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Same in-place merge as <see cref="MergeChanges"/>, to avoid flicker.</summary>
    private void MergeBranches(IReadOnlyList<Branch> branches)
    {
        {
            for (int i = Branches.Count - 1; i >= 0; i--)
            {
                if (!branches.Any(b => b.Name == Branches[i].Name)) Branches.RemoveAt(i);
            }

            for (int i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                var existing = Branches.FirstOrDefault(b => b.Name == branch.Name);

                if (existing == null)
                {
                    Branches.Insert(Math.Min(i, Branches.Count), branch);
                }
                else if (existing.IsCurrent != branch.IsCurrent)
                {
                    Branches[Branches.IndexOf(existing)] = branch;
                }
            }

            CurrentBranch = Branches.FirstOrDefault(b => b.IsCurrent);
        }
    }

    private async Task LoadDiffAsync(FileChange change)
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath)) return;

        var token = Guid.NewGuid();
        _diffRequestToken = token;
        IsLoadingDiff = true;

        try
        {
            var diff = await _gitService.GetDiffAsync(repoPath, change);

            // The user selected another file while this one was loading.
            if (_diffRequestToken != token) return;
            CurrentDiff = diff;
        }
        catch (Exception ex)
        {
            if (_diffRequestToken != token) return;
            CurrentDiff = null;
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            if (_diffRequestToken == token) IsLoadingDiff = false;
        }
    }

    // MARK: - Identity

    /// <summary>Adds a profile, or replaces the one with the same id.</summary>
    public void SaveIdentity(GitIdentity identity)
    {
        var existing = SavedIdentities.FirstOrDefault(i => i.Id == identity.Id);
        if (existing == null)
        {
            SavedIdentities.Add(identity);
        }
        else
        {
            SavedIdentities[SavedIdentities.IndexOf(existing)] = identity;
        }

        Save();
    }

    public void RemoveIdentity(string id)
    {
        var existing = SavedIdentities.FirstOrDefault(i => i.Id == id);
        if (existing == null) return;

        SavedIdentities.Remove(existing);
        Save();
    }

    /// <summary>
    /// Switches who commits here. Writes the identity into this repository only,
    /// never the global config: the point of profiles is that one repository can
    /// differ from the rest without the user having to remember it does.
    ///
    /// Rewriting the remote is optional and separate, because it changes how the
    /// repository authenticates, not who it credits.
    /// </summary>
    public async Task SetIdentityAsync(GitIdentity identity, bool fixRemoteUrl)
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath) || IsSwitchingIdentity) return;

        IsSwitchingIdentity = true;
        try
        {
            await _gitService.SetIdentityAsync(repoPath, identity.Name, identity.Email);

            if (fixRemoteUrl && !string.IsNullOrWhiteSpace(identity.GitHubUsername))
            {
                try
                {
                    await _gitService.SetRemoteUsernameAsync(repoPath, identity.GitHubUsername);
                }
                catch (Exception ex)
                {
                    // The identity itself did change; say what didn't rather than
                    // reporting the whole switch as a failure.
                    ErrorMessage = $"Identity changed, but the remote URL was left alone: {ex.Message}";
                    return;
                }
            }

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSwitchingIdentity = false;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Hands a GitHub token to git's own credential helper. The app stores no
    /// copy of it — this exists because push and fetch authenticate separately
    /// from who the commits credit, and that surprises people.
    /// </summary>
    public async Task<bool> SaveGitHubTokenAsync(string token, string username)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(username)) return false;

        try
        {
            await _gitService.ApproveCredentialAsync(username, token);
            ErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    public async Task StageAsync(FileChange change)
    {
        await RunGitAsync(path => _gitService.StageAsync(path, change.Path));
    }

    /// <summary>
    /// Stages or unstages every listed file. Takes the paths explicitly rather
    /// than running "add ." so it can also go the other way, and so it never
    /// picks up a file that appeared after the list was rendered.
    /// </summary>
    public async Task SetAllStagedAsync(bool staged)
    {
        var paths = Changes.Select(c => c.Path).ToList();
        if (paths.Count == 0) return;

        await RunGitAsync(path => staged
            ? _gitService.StageAsync(path, paths)
            : _gitService.UnstageAsync(path, paths));
    }

    [RelayCommand]
    public async Task UnstageAsync(FileChange change)
    {
        await RunGitAsync(path => _gitService.UnstageAsync(path, change.Path));
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    public async Task CommitAsync()
    {
        if (!CanCommit) return;

        var summary = CommitSummary;
        var description = CommitDescription;
        await RunGitAsync(path => _gitService.CommitAsync(path, summary, description));

        // Keep the message on failure — it is the one thing the user typed by hand.
        if (HasError) return;

        CommitSummary = string.Empty;
        CommitDescription = string.Empty;
        CommitType = null;
        CommitScope = string.Empty;
    }

    /// <summary>
    /// Undoes a change. Destructive — the caller is expected to have confirmed
    /// with the user first.
    /// </summary>
    public async Task DiscardAsync(FileChange change)
    {
        await RunGitAsync(async path =>
        {
            // Re-read the status first: the file may have moved on since the
            // list was rendered, and discarding by a stale status runs the
            // wrong git commands.
            var current = (await _gitService.GetStatusAsync(path)).FirstOrDefault(c => c.Path == change.Path);
            if (current == null) return;

            if (SelectedChange?.Path == current.Path) SelectedChange = null;
            await _gitService.DiscardAsync(path, current);
        });
    }

    /// <summary>
    /// Switches branches. Uses <see cref="Branch.CheckoutName"/>, not the display
    /// name: checking out "origin/main" literally would detach HEAD.
    /// </summary>
    public async Task CheckoutBranchAsync(Branch branch)
    {
        if (branch == null) return;
        await SwitchBranchAsync(branch.CheckoutName);
    }

    public async Task SwitchBranchAsync(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return;

        await RunGitAsync(path => _gitService.CheckoutAsync(path, branchName));
        if (!HasError) PromoteRecentBranch(branchName);
    }

    /// <summary>Creates a branch from the current HEAD and switches to it.</summary>
    public async Task CreateBranchAsync(string branchName)
    {
        var trimmed = branchName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return;

        await RunGitAsync(path => _gitService.CreateBranchAsync(path, trimmed));
        if (!HasError) PromoteRecentBranch(trimmed);
    }

    public async Task MergeBranchAsync(Branch branch)
    {
        if (branch == null) return;
        await RunGitAsync(path => _gitService.MergeAsync(path, branch.Name));
    }

    [RelayCommand]
    public async Task FetchAsync()
    {
        await RunGitAsync(async path =>
        {
            await _gitService.FetchAsync(path);
            _lastFetch = DateTimeOffset.UtcNow;
        });
    }

    /// <summary>
    /// Fetch triggered by the window regaining focus, rate-limited so alt-tabbing
    /// doesn't hit the network on every switch.
    /// </summary>
    public async Task FetchOnActivationAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        if (DateTimeOffset.UtcNow - _lastFetch < MinFetchInterval)
        {
            await RefreshAsync();
            return;
        }

        await FetchAsync();
    }

    [RelayCommand]
    public async Task PushAsync()
    {
        var publish = !HasUpstream;
        await RunGitAsync(async path =>
        {
            try
            {
                if (publish) await _gitService.PushPublishAsync(path);
                else await _gitService.PushAsync(path);
            }
            catch (GitException ex) when (IsRejectedForNewRemoteWork(ex))
            {
                // The ahead/behind counts are only as fresh as the last fetch, so
                // a push can be the first thing to learn the remote moved. Fetch
                // before giving up: the refresh that follows turns the button into
                // the sync that will actually work.
                await _gitService.FetchAsync(path);
                _lastFetch = DateTimeOffset.UtcNow;
                throw;
            }
        });
    }

    /// <summary>
    /// A push git refused because the upstream has commits this clone has never
    /// seen — the one rejection that a fetch changes the answer to.
    /// </summary>
    private static bool IsRejectedForNewRemoteWork(GitException ex) =>
        ex.RawError.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
        || ex.RawError.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one action the sync button performs, chosen from the current state.
    ///
    /// A diverged branch pulls before it pushes: git rejects a non-fast-forward
    /// push, so preferring push there left the button doing nothing but printing
    /// a rejection. Both run inside one operation so a merge that stops on a
    /// conflict stops the push with it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSync))]
    public async Task SyncAsync()
    {
        switch (CurrentSyncAction)
        {
            case SyncAction.Publish:
            case SyncAction.Push:
                await PushAsync();
                break;

            case SyncAction.Pull:
                await PullAsync();
                break;

            case SyncAction.PullThenPush:
                await RunGitAsync(async path =>
                {
                    await _gitService.PullDivergentAsync(path);
                    await _gitService.PushAsync(path);
                });
                break;
        }
    }

    /// <summary>
    /// Fills in the conventional-commit type and scope from what is staged.
    /// Needs numstat, which only git can answer, hence async.
    /// </summary>
    public async Task SuggestCommitTypeAsync()
    {
        var repoPath = RepositoryPath;
        var staged = StagedChanges;
        if (staged.Count == 0) return;

        IReadOnlyDictionary<string, ConventionalCommitSuggester.LineStats>? numstat = null;
        if (!string.IsNullOrEmpty(repoPath))
        {
            try
            {
                numstat = await _gitService.GetStagedNumstatAsync(repoPath);
            }
            catch
            {
                // A suggestion is a convenience; fall back to the status-only rules.
            }
        }

        CommitScope = ConventionalCommitSuggester.SuggestScope(staged) ?? string.Empty;
        CommitType = ConventionalCommitSuggester.SuggestType(staged, numstat);
    }

    /// <summary>
    /// Rewrites the "type(scope): " prefix of the summary, keeping whatever the
    /// user already typed after it.
    /// </summary>
    public void ApplyConventionalPrefix()
    {
        if (CommitType == null) return;

        var scope = CommitScope.Trim();
        var scopePart = scope.Length == 0 ? string.Empty : $"({scope})";
        var prefix = $"{CommitType.Name}{scopePart}: ";

        var separator = CommitSummary.IndexOf(": ", StringComparison.Ordinal);
        CommitSummary = separator >= 0
            ? prefix + CommitSummary.Substring(separator + 2)
            : prefix + CommitSummary;
    }

    [RelayCommand]
    public async Task PullAsync()
    {
        await RunGitAsync(path => _gitService.PullAsync(path));
    }

    /// <summary>Runs a git operation, then refreshes — with uniform busy and error handling.</summary>
    private async Task RunGitAsync(Func<string, Task> operation)
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath) || IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operation(repoPath);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    // MARK: - Auto refresh

    private void StartAutoRefresh()
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath)) return;

        var gitDir = Path.Combine(repoPath, ".git");
        if (!Directory.Exists(gitDir)) return;

        _debounceTimer = _dispatcher.CreateTimer();
        _debounceTimer.Interval = WatcherDebounce;
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += (_, _) => _ = RefreshAsync();

        _gitWatcher = new FileSystemWatcher(gitDir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true
        };

        _gitWatcher.Changed += OnGitDirectoryChanged;
        _gitWatcher.Created += OnGitDirectoryChanged;
        _gitWatcher.Deleted += OnGitDirectoryChanged;
        _gitWatcher.Renamed += OnGitDirectoryChanged;
        _gitWatcher.EnableRaisingEvents = true;

        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = PollInterval;
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += (_, _) => _ = RefreshAsync();
        _pollTimer.Start();
    }

    private void OnGitDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return;

        // Raised on a threadpool thread. A single `git add` fires dozens of
        // these; restarting the timer collapses the burst into one refresh.
        _dispatcher.TryEnqueue(() =>
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        });
    }

    private void StopAutoRefresh()
    {
        if (_gitWatcher != null)
        {
            _gitWatcher.EnableRaisingEvents = false;
            _gitWatcher.Changed -= OnGitDirectoryChanged;
            _gitWatcher.Created -= OnGitDirectoryChanged;
            _gitWatcher.Deleted -= OnGitDirectoryChanged;
            _gitWatcher.Renamed -= OnGitDirectoryChanged;
            _gitWatcher.Dispose();
            _gitWatcher = null;
        }

        _debounceTimer?.Stop();
        _debounceTimer = null;
        _pollTimer?.Stop();
        _pollTimer = null;
    }

    public void Dispose() => StopAutoRefresh();
}
