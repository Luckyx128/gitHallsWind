using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.App.Services;
using GitHalls.Core.Commits;
using GitHalls.Core.Diff;
using GitHalls.Core.Git;
using GitHalls.Core.GitHub;
using GitHalls.Core.Markdown;
using GitHalls.Core.Models;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.Diagnostics;

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
    private readonly GitHubCli _gitHubCli = new();
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

    public void ClearError()
    {
        ErrorMessage = null;
        PullBlockedByLocalChanges = false;
    }

    public ObservableCollection<FileChange> Changes { get; } = new();
    public ObservableCollection<Commit> Commits { get; } = new();
    public ObservableCollection<Branch> Branches { get; } = new();
    public ObservableCollection<string> RecentRepositories { get; } = new();

    /// <summary>Branches recently checked out in the current repository, most recent first.</summary>
    public ObservableCollection<string> RecentBranchNames { get; } = new();

    [ObservableProperty]
    private FileChange? _selectedChange;

    /// <summary>
    /// The diff of the side currently being looked at. Kept as its own property
    /// rather than computed at the call site because the window's PropertyChanged
    /// switch is what drives the pane.
    /// </summary>
    [ObservableProperty]
    private FileDiff? _currentDiff;

    /// <summary>
    /// Working tree against the index — what is still to be staged. Its old side
    /// is the index, which is what lets a selection out of it be applied there.
    /// </summary>
    [ObservableProperty]
    private FileDiff? _workingTreeDiff;

    /// <summary>Index against HEAD — what is staged, and so what can be taken back.</summary>
    [ObservableProperty]
    private FileDiff? _indexDiff;

    /// <summary>
    /// Which of the two the pane is showing. The old single diff against HEAD
    /// could show everything at once but could not be applied to the index, so
    /// staging a selection means choosing a side first.
    /// </summary>
    [ObservableProperty]
    private DiffSide _diffSideSelection = DiffSide.WorkingTree;

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

    /// <summary>The bytes behind the selected binary file, when it is one.</summary>
    [ObservableProperty]
    private BinaryFileContents? _currentDiffPreview;

    [ObservableProperty]
    private BinaryFileContents? _commitFilePreview;

    /// <summary>
    /// git refused a pull because it would have written over uncommitted work.
    /// The error bar turns this into an offer rather than a dead end.
    /// </summary>
    [ObservableProperty]
    private bool _pullBlockedByLocalChanges;

    /// <summary>
    /// The repository's README, as blocks to lay out.
    ///
    /// Read from the working tree, so it is always this branch's own copy:
    /// checking out another branch rewrites the file on disk, and this re-reads
    /// it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadme))]
    private IReadOnlyList<MarkdownBlock> _readme = Array.Empty<MarkdownBlock>();

    [ObservableProperty]
    private string? _readmeFileName;

    public bool HasReadme => Readme.Count > 0;

    /// <summary>
    /// Repository and branch the README on screen was read for. A status
    /// refresh happens on every window activation, and re-reading a file that
    /// cannot have changed is work for nothing.
    /// </summary>
    private string? _readmeKey;

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

    public GitgraphViewModel GitgraphViewModel { get; }

    public RepositoryViewModel(GitService gitService, SettingsStore settingsStore)
    {
        _gitService = gitService;
        _settingsStore = settingsStore;
        
        GitgraphViewModel = new GitgraphViewModel(_gitService, this);

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
        
        Changes.Clear();
        Commits.Clear();
        Branches.Clear();
        Readme = Array.Empty<MarkdownBlock>();
        ReadmeFileName = null;

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
            WorkingTreeDiff = null;
            IndexDiff = null;
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

            CommitFilePreview = diff.IsBinary
                ? await LoadPreviewAsync(repoPath, diff.FilePath, $"{commit.Hash}^", commit.Hash)
                : null;

            if (_commitFileDiffRequestToken != token) CommitFilePreview = null;
        }
        catch (Exception ex)
        {
            if (_commitFileDiffRequestToken != token) return;
            CommitFileDiff = null;
            CommitFilePreview = null;
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            if (_commitFileDiffRequestToken == token) IsLoadingCommitFileDiff = false;
        }
    }

    /// <summary>
    /// Both sides of a binary file. A null afterRevision means the working tree
    /// — the side no revision names.
    /// </summary>
    private async Task<BinaryFileContents> LoadPreviewAsync(string repoPath, string filePath, string beforeRevision, string? afterRevision)
    {
        var before = await _gitService.GetBlobAsync(repoPath, beforeRevision, filePath);

        var after = afterRevision == null
            ? await GitService.GetWorkingTreeBytesAsync(repoPath, filePath)
            : await _gitService.GetBlobAsync(repoPath, afterRevision, filePath);

        return new BinaryFileContents(filePath, before, after);
    }

    // MARK: - Readme

    /// <summary>
    /// Longest README worth laying out. Past this it is a data file that
    /// happens to be called README, and rendering it would only hang the pane.
    /// </summary>
    private const int ReadmeSizeLimit = 512 * 1024;

    private async Task LoadReadmeIfNeededAsync(string repoPath, string? branch)
    {
        var key = $"{repoPath}#{branch}";
        if (_readmeKey == key) return;
        _readmeKey = key;

        try
        {
            var names = Directory.EnumerateFiles(repoPath).Select(Path.GetFileName).OfType<string>().ToList();
            var name = ReadmeFinder.Pick(names);

            if (name != null)
            {
                var full = Path.Combine(repoPath, name);
                if (new FileInfo(full).Length <= ReadmeSizeLimit)
                {
                    var text = await File.ReadAllTextAsync(full);
                    ReadmeFileName = name;
                    Readme = MarkdownParser.Parse(text);
                    return;
                }
            }
        }
        catch (IOException)
        {
            // A README that cannot be read is the same as none for this pane.
        }

        ReadmeFileName = null;
        Readme = Array.Empty<MarkdownBlock>();
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

    /// <summary>
    /// Not a command any more: the watcher, the poll and every git operation
    /// call it, and the toolbar has no refresh button to bind it to.
    /// </summary>
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

        // Hooked here rather than on open: this runs after a checkout, a pull
        // and a merge too, which is every way the branch's README can change.
        await LoadReadmeIfNeededAsync(repoPath, CurrentBranch?.Name);

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
        OnPropertyChanged(nameof(ConflictedChanges));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(ConflictSummary));
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
            // Both sides under the one token: a slow answer for a file the user
            // has already moved on from must not land on either of them.
            var working = await _gitService.GetWorkingTreeDiffAsync(repoPath, change);
            var index = await _gitService.GetIndexDiffAsync(repoPath, change);

            if (_diffRequestToken != token) return;

            WorkingTreeDiff = working;
            IndexDiff = index;
            DiffSideSelection = PreferredSide(working, index);
            ApplyDiffSide();

            var diff = CurrentDiff;

            // A binary file has no text to show, so the pane shows the file.
            // Loaded only for that file, not for every change in the list.
            CurrentDiffPreview = diff is { IsBinary: true }
                ? await LoadPreviewAsync(repoPath, diff.FilePath, "HEAD", afterRevision: null)
                : null;

            if (_diffRequestToken != token) CurrentDiffPreview = null;
        }
        catch (Exception ex)
        {
            if (_diffRequestToken != token) return;
            WorkingTreeDiff = null;
            IndexDiff = null;
            CurrentDiff = null;
            CurrentDiffPreview = null;
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            if (_diffRequestToken == token) IsLoadingDiff = false;
        }
    }

    // MARK: - Staging a selection

    /// <summary>
    /// Which side to open on. Unstaged is where the work is, so it wins whenever
    /// it has anything; a fully staged file opens on what it has instead of on
    /// an empty pane.
    /// </summary>
    private static DiffSide PreferredSide(FileDiff? working, FileDiff? index)
    {
        if (HasChanges(working)) return DiffSide.WorkingTree;
        if (HasChanges(index)) return DiffSide.Index;
        return DiffSide.WorkingTree;
    }

    private static bool HasChanges(FileDiff? diff) =>
        diff != null && (diff.IsBinary || diff.Additions > 0 || diff.Deletions > 0);

    partial void OnDiffSideSelectionChanged(DiffSide value) => ApplyDiffSide();

    private void ApplyDiffSide() =>
        CurrentDiff = DiffSideSelection == DiffSide.Index ? IndexDiff : WorkingTreeDiff;

    /// <summary>
    /// Moves just <paramref name="lineIndices"/> into or out of the index,
    /// depending on which side is being shown. Runs through the same wrapper as
    /// every other git action, so busy state, errors and the refresh behave the
    /// same way.
    /// </summary>
    public async Task ApplySelectionAsync(IReadOnlySet<int> lineIndices)
    {
        var diff = CurrentDiff;
        if (diff == null || lineIndices.Count == 0) return;

        var reverse = DiffSideSelection == DiffSide.Index;
        var patch = PatchBuilder.Build(diff, lineIndices, reverse ? PatchDirection.Reverse : PatchDirection.Forward);
        if (patch == null) return;

        var path = diff.FilePath;

        await RunGitAsync(async repoPath =>
        {
            await _gitService.ApplyPatchAsync(repoPath, patch, reverse);
        });

        await ReloadDiffForAsync(path);
    }

    /// <summary>The same, for every changed line of one hunk.</summary>
    public Task ApplyHunkAsync(DiffHunk hunk)
    {
        var diff = CurrentDiff;
        return diff == null ? Task.CompletedTask : ApplySelectionAsync(PatchBuilder.ChangedLinesOf(diff, hunk));
    }

    /// <summary>
    /// Reloads both sides after a patch landed, and moves off a side the patch
    /// just emptied so the pane never sits blank on a file that still has changes.
    /// </summary>
    private async Task ReloadDiffForAsync(string path)
    {
        var change = Changes.FirstOrDefault(c => c.Path == path);
        if (change == null)
        {
            WorkingTreeDiff = null;
            IndexDiff = null;
            CurrentDiff = null;
            return;
        }

        var side = DiffSideSelection;
        await LoadDiffAsync(change);

        // LoadDiffAsync picks the side a fresh selection would open on; keep the
        // one being worked in unless it has nothing left to show.
        var stillThere = side == DiffSide.Index ? HasChanges(IndexDiff) : HasChanges(WorkingTreeDiff);
        if (stillThere && DiffSideSelection != side) DiffSideSelection = side;
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

    // MARK: - Pull request

    /// <summary>
    /// The "origin" URL, read on demand. Nothing on screen shows it, so it is
    /// not worth a git process on every refresh.
    /// </summary>
    public Task<string?> GetRemoteUrlAsync() => string.IsNullOrEmpty(RepositoryPath)
        ? Task.FromResult<string?>(null)
        : _gitService.GetRemoteUrlAsync(RepositoryPath);

    /// <summary>Remote branches, short names only — what a pull request can be opened against.</summary>
    public IReadOnlyList<string> RemoteBranchNames => Branches
        .Where(b => b.IsRemote)
        .Select(b => Branch.RemoteShortName(b.Name))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The title and description a new pull request opens with: one commit
    /// ahead of the base means that commit says everything, several mean only
    /// the branch name covers all of them.
    ///
    /// Read at the moment the dialog opens rather than kept on the view model:
    /// it costs a git process, and nothing else on screen needs the answer.
    /// </summary>
    public async Task<PullRequestDraft> SuggestPullRequestAsync(string? baseBranch)
    {
        var path = RepositoryPath;
        var head = CurrentBranch?.Name;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(head)) return PullRequestDraft.Empty;

        // The picker shows the branch as GitHub names it ("main"); the commits
        // are counted against the copy this clone has of it.
        var baseRef = string.IsNullOrWhiteSpace(baseBranch)
            ? await _gitService.GetDefaultBaseRefAsync(path)
            : $"origin/{baseBranch}";

        IReadOnlyList<Commit> commits = Array.Empty<Commit>();
        if (baseRef != null) commits = await _gitService.GetCommitsAheadAsync(path, baseRef);

        return PullRequestDraft.From(commits, head);
    }

    /// <summary>
    /// Pushes the current branch and opens a pull request for it, returning the
    /// URL to show — the caller opens it, since a browser is the shell's job.
    ///
    /// The push is not optional: GitHub has no page for a branch it has never
    /// seen. With `gh` installed the PR is created outright; without it, the
    /// prefilled GitHub page is the fallback and the user presses the button.
    /// Port of RepositoryViewModel.createPullRequest.
    /// </summary>
    public async Task<string?> CreatePullRequestAsync(string title, string description, string? baseBranch)
    {
        var head = CurrentBranch?.Name;
        if (string.IsNullOrEmpty(head)) return null;

        string? url = null;
        await RunGitAsync(async path =>
        {
            if (HasUpstream) await _gitService.PushAsync(path);
            else await _gitService.PushPublishAsync(path);

            if (await _gitHubCli.IsAvailableAsync())
            {
                url = await _gitHubCli.CreatePullRequestAsync(path, title, description, baseBranch);
                return;
            }

            var remote = await _gitService.GetRemoteUrlAsync(path);
            url = PullRequestUrl.ForBrowser(remote, head, baseBranch, title, description)
                ?? throw new GitException("This repository has no GitHub remote to open a pull request on.", remote ?? string.Empty);
        });

        return HasError ? null : url;
    }

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
                PullBlockedByLocalChanges = false;
                await RunGitAsync(async path =>
                {
                    try
                    {
                        await _gitService.PullDivergentAsync(path);
                    }
                    catch (GitException ex) when (PullDiagnostics.IsBlockedByLocalChanges(ex.RawError))
                    {
                        // Same offer as a plain pull: the sync button is where
                        // this is most often hit.
                        PullBlockedByLocalChanges = true;
                        throw;
                    }

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
        PullBlockedByLocalChanges = false;

        await RunGitAsync(async path =>
        {
            try
            {
                await _gitService.PullAsync(path);
            }
            catch (GitException ex) when (PullDiagnostics.IsBlockedByLocalChanges(ex.RawError))
            {
                // Not a dead end: the changes can be set aside for the pull and
                // put back. The error bar turns this flag into that offer.
                PullBlockedByLocalChanges = true;
                throw;
            }
        });
    }

    /// <summary>The offer a blocked pull turns into: stash, pull, restore.</summary>
    [RelayCommand]
    public async Task PullWithStashAsync()
    {
        PullBlockedByLocalChanges = false;

        var conflicted = false;
        await RunGitAsync(async path => conflicted = await _gitService.PullAutostashAsync(path));

        // Not an error — the pull worked. But saying nothing would leave the
        // user in a conflicted tree with a stash nobody mentioned.
        if (conflicted && !HasError) ErrorMessage = "Pulled, but your local changes could not be put back cleanly.\n\nThey are safe in the stash: resolve the conflicts in the affected files, then drop the leftover stash entry.";
    }

    // MARK: - Conflicts

    /// <summary>
    /// Files git could not merge on its own. Nothing else can be committed
    /// until these are dealt with, so they are worth calling out rather than
    /// leaving in the list looking like ordinary changes.
    /// </summary>
    public IReadOnlyList<FileChange> ConflictedChanges =>
        Changes.Where(c => c.IndexStatus == FileChangeStatus.Unmerged || c.WorkTreeStatus == FileChangeStatus.Unmerged).ToList();

    public bool HasConflicts => ConflictedChanges.Count > 0;

    public string ConflictSummary => ConflictedChanges.Count == 1
        ? "1 file has conflicts. Resolve it, then mark it resolved."
        : $"{ConflictedChanges.Count} files have conflicts. Resolve them, then mark them resolved.";

    /// <summary>
    /// Tells git the file is settled. Resolving <em>is</em> staging — there is
    /// no separate "resolved" state, which is why this reuses stage.
    /// </summary>
    public async Task MarkResolvedAsync(FileChange change)
    {
        await RunGitAsync(path => _gitService.StageAsync(path, change.Path));
    }

    public void OpenInEditor(FileChange change, ExternalEditor editor)
    {
        var repoPath = RepositoryPath;
        if (string.IsNullOrEmpty(repoPath)) return;

        var executable = ExternalEditors.Locate(editor);
        if (executable == null) return;

        var full = Path.Combine(repoPath, change.Path.Replace('/', Path.DirectorySeparatorChar));

        try
        {
            Process.Start(new ProcessStartInfo(executable, full) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open {editor.Name}: {ex.Message}";
        }
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
