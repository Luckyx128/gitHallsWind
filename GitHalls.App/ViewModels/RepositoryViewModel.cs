using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.Core.Git;
using GitHalls.Core.Models;
using System.Collections.ObjectModel;

namespace GitHalls.App.ViewModels;

public partial class RepositoryViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _timerCts;

    [ObservableProperty]
    private string? _repositoryPath;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<FileChange> Changes { get; } = new();
    public ObservableCollection<Commit> Commits { get; } = new();
    public ObservableCollection<Branch> Branches { get; } = new();

    [ObservableProperty]
    private FileChange? _selectedChange;

    [ObservableProperty]
    private FileDiff? _currentDiff;

    [ObservableProperty]
    private Branch? _currentBranch;

    public RepositoryViewModel(GitService gitService)
    {
        _gitService = gitService;
    }

    partial void OnRepositoryPathChanged(string? value)
    {
        StopAutoRefresh();
        if (!string.IsNullOrEmpty(value))
        {
            StartAutoRefresh();
            _ = RefreshAsync();
        }
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

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath) || IsBusy) return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var status = await _gitService.GetStatusAsync(RepositoryPath);
            
            // Smart merge to prevent ListView flickering
            var toRemove = Changes.Where(c => !status.Any(s => s.Path == c.Path)).ToList();
            foreach (var c in toRemove)
            {
                Changes.Remove(c);
            }
            
            for (int i = 0; i < status.Count; i++)
            {
                var change = status[i];
                var existing = Changes.FirstOrDefault(c => c.Path == change.Path);
                
                if (existing != null)
                {
                    if (existing.IndexStatus != change.IndexStatus || existing.WorkTreeStatus != change.WorkTreeStatus)
                    {
                        // Replace to trigger UI update for this specific item only
                        int index = Changes.IndexOf(existing);
                        Changes[index] = change;
                    }
                }
                else
                {
                    Changes.Add(change);
                }
            }

            if (SelectedChange != null)
            {
                // Reload diff for selected change if it still exists
                var stillExists = Changes.FirstOrDefault(c => c.Path == SelectedChange.Path);
                if (stillExists != null)
                {
                    SelectedChange = stillExists;
                }
                else
                {
                    SelectedChange = null;
                }
            }

            var commits = await _gitService.GetLogAsync(RepositoryPath);
            Commits.Clear();
            foreach (var c in commits) Commits.Add(c);

            var branches = await _gitService.GetBranchesAsync(RepositoryPath);
            Branches.Clear();
            foreach (var b in branches) Branches.Add(b);

            CurrentBranch = branches.FirstOrDefault(b => b.IsCurrent);

            if (CurrentBranch != null)
            {
                bool hasUpstream = await _gitService.HasUpstreamAsync(RepositoryPath, CurrentBranch.Name);
                PushButtonLabel = hasUpstream ? "Push" : "Publish Branch";
            }
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

    private async Task LoadDiffAsync(FileChange change)
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;

        try
        {
            bool isUntracked = change.IndexStatus == FileChangeStatus.Untracked && change.WorkTreeStatus == FileChangeStatus.Untracked;
            CurrentDiff = await _gitService.GetDiffAsync(RepositoryPath, change.Path, change.IsStaged, isUntracked);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load diff: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StageAsync(FileChange change)
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        await _gitService.StageAsync(RepositoryPath, change.Path);
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        await _gitService.StageAsync(RepositoryPath, ".");
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task UnstageAsync(FileChange change)
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        await _gitService.UnstageAsync(RepositoryPath, change.Path);
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task CommitAsync(string message)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || string.IsNullOrWhiteSpace(message)) return;
        
        IsBusy = true;
        try
        {
            await _gitService.CommitAsync(RepositoryPath, message);
            await RefreshAsync();
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

    public async Task CheckoutBranchAsync(Branch branch)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || branch == null) return;
        
        IsBusy = true;
        try
        {
            await _gitService.CheckoutAsync(RepositoryPath, branch.Name);
            await RefreshAsync();
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

    [ObservableProperty]
    private string _pushButtonLabel = "Push";

    [RelayCommand]
    public async Task PushAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        
        IsBusy = true;
        try
        {
            if (PushButtonLabel == "Publish Branch")
            {
                // Set upstream dynamically (assuming origin is the target remote)
                await _gitService.PushPublishAsync(RepositoryPath);
            }
            else
            {
                await _gitService.PushAsync(RepositoryPath);
            }
            await RefreshAsync();
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

    [RelayCommand]
    public async Task PullAsync()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;
        
        IsBusy = true;
        try
        {
            await _gitService.PullAsync(RepositoryPath);
            await RefreshAsync();
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

    private System.IO.FileSystemWatcher? _gitWatcher;

    private void StartAutoRefresh()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return;

        var gitDir = System.IO.Path.Combine(RepositoryPath, ".git");
        if (!System.IO.Directory.Exists(gitDir)) return;

        _gitWatcher = new System.IO.FileSystemWatcher(gitDir)
        {
            NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName,
            IncludeSubdirectories = true
        };

        _gitWatcher.Changed += OnGitDirectoryChanged;
        _gitWatcher.Created += OnGitDirectoryChanged;
        _gitWatcher.Deleted += OnGitDirectoryChanged;
        _gitWatcher.Renamed += OnGitDirectoryChanged;

        _gitWatcher.EnableRaisingEvents = true;

        // Also start the polling fallback just in case
        _timerCts = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _refreshTimer.WaitForNextTickAsync(_timerCts.Token))
                {
                    await dispatcher.EnqueueAsync(() => RefreshAsync());
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void OnGitDirectoryChanged(object sender, System.IO.FileSystemEventArgs e)
    {
        // Debounce or filter out lock files if needed
        if (e.FullPath.EndsWith(".lock")) return;

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null)
        {
            _ = dispatcher.EnqueueAsync(() => RefreshAsync());
        }
        else
        {
            // If we are on a background thread from FileSystemWatcher, we must get the dispatcher from somewhere else.
            // But we stored it in the window. 
            // Better to dispatch to Main window's dispatcher.
            var window = (App.Current as App)?.MainWindow;
            if (window != null)
            {
                _ = window.DispatcherQueue.EnqueueAsync(() => RefreshAsync());
            }
        }
    }

    private void StopAutoRefresh()
    {
        if (_gitWatcher != null)
        {
            _gitWatcher.EnableRaisingEvents = false;
            _gitWatcher.Dispose();
            _gitWatcher = null;
        }
        _timerCts?.Cancel();
        _refreshTimer?.Dispose();
    }
}
