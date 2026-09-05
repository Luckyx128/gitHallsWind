using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.App.Services;
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

    [ObservableProperty]
    private FileChange? _selectedChange;

    [ObservableProperty]
    private FileDiff? _currentDiff;

    [ObservableProperty]
    private Branch? _currentBranch;

    [ObservableProperty]
    private string _pushButtonLabel = "Push";

    /// <summary>
    /// True while <see cref="RefreshAsync"/> is repopulating <see cref="Branches"/>.
    /// The branch picker has to ignore its own SelectionChanged during that window,
    /// or repopulating the list checks out a branch nobody asked for.
    /// </summary>
    public bool IsApplyingBranches { get; private set; }

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
        foreach (var path in settings.RecentRepositories.Where(Directory.Exists))
        {
            RecentRepositories.Add(path);
        }

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
        ErrorMessage = null;

        if (string.IsNullOrEmpty(value)) return;

        PromoteRecent(value);
        StartAutoRefresh();
        _ = RefreshAsync();
    }

    partial void OnSelectedChangeChanged(FileChange? value)
    {
        if (value != null && !string.IsNullOrEmpty(RepositoryPath))
        {
            _ = LoadDiffAsync(value);
        }
        else
        {
            CurrentDiff = null;
        }
    }

    private void PromoteRecent(string path)
    {
        var existing = RecentRepositories.IndexOf(path);
        if (existing >= 0) RecentRepositories.RemoveAt(existing);
        RecentRepositories.Insert(0, path);
        while (RecentRepositories.Count > 10) RecentRepositories.RemoveAt(RecentRepositories.Count - 1);

        _ = _settingsStore.SaveAsync(new AppSettings
        {
            RecentRepositories = RecentRepositories.ToList(),
            LastOpenedRepository = path
        });
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

        var commits = await _gitService.GetLogAsync(repoPath);
        Commits.Clear();
        foreach (var c in commits) Commits.Add(c);

        var branches = await _gitService.GetBranchesAsync(repoPath);
        MergeBranches(branches);

        if (CurrentBranch != null)
        {
            bool hasUpstream = await _gitService.HasUpstreamAsync(repoPath, CurrentBranch.Name);
            PushButtonLabel = hasUpstream ? "Push" : "Publish Branch";
        }
    }

    /// <summary>Replaces only what actually changed, so the list doesn't flicker.</summary>
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
    }

    /// <summary>
    /// Same in-place merge as <see cref="MergeChanges"/>, and for a sharper
    /// reason: clearing this collection nulls the branch picker's SelectedItem,
    /// and repopulating it then raises SelectionChanged with a brand-new object
    /// — which used to read as "the user picked a branch" and fire a checkout.
    /// </summary>
    private void MergeBranches(IReadOnlyList<Branch> branches)
    {
        IsApplyingBranches = true;
        try
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
        finally
        {
            IsApplyingBranches = false;
        }
    }

    private async Task LoadDiffAsync(FileChange change)
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;

        try
        {
            CurrentDiff = await _gitService.GetDiffAsync(RepositoryPath, change);
        }
        catch (Exception ex)
        {
            CurrentDiff = null;
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StageAsync(FileChange change)
    {
        await RunGitAsync(path => _gitService.StageAsync(path, change.Path));
    }

    [RelayCommand]
    public async Task StageAllAsync()
    {
        await RunGitAsync(path => _gitService.StageAsync(path, "."));
    }

    [RelayCommand]
    public async Task UnstageAsync(FileChange change)
    {
        await RunGitAsync(path => _gitService.UnstageAsync(path, change.Path));
    }

    [RelayCommand]
    public async Task CommitAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        await RunGitAsync(path => _gitService.CommitAsync(path, message));
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

    public async Task CheckoutBranchAsync(Branch branch)
    {
        if (branch == null) return;
        await RunGitAsync(path => _gitService.CheckoutAsync(path, branch.Name));
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
        var publish = PushButtonLabel == "Publish Branch";
        await RunGitAsync(path => publish ? _gitService.PushPublishAsync(path) : _gitService.PushAsync(path));
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
