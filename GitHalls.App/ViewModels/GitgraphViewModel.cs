using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.Core.Git;
using GitHalls.Core.Graph;
using GitHalls.Core.Models;

namespace GitHalls.App.ViewModels;

public partial class GitgraphViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private readonly RepositoryViewModel _repoViewModel;

    [ObservableProperty]
    private ObservableCollection<GraphRow> _rows = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private GraphRow? _selectedRow;

    [ObservableProperty]
    private ObservableCollection<GitHalls.App.Models.GitTreeFileNode> _selectedCommitTree = new();

    public GitgraphViewModel(GitService gitService, RepositoryViewModel repoViewModel)
    {
        _gitService = gitService;
        _repoViewModel = repoViewModel;
        
        _repoViewModel.PropertyChanged += RepoViewModel_PropertyChanged;
    }

    private void RepoViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.RepositoryPath))
        {
            _ = LoadAsync();
        }
    }

    private GraphRow? _lastExpandedRow;

    partial void OnSelectedRowChanged(GraphRow? value)
    {
        if (_lastExpandedRow != null)
        {
            _lastExpandedRow.IsExpanded = false;
        }

        if (value != null)
        {
            value.IsExpanded = true;
            _lastExpandedRow = value;
            _repoViewModel.SelectedCommit = value.Commit;
            _ = LoadTreeAsync(value.Commit);
        }
        else
        {
            SelectedCommitTree.Clear();
            _lastExpandedRow = null;
        }
    }

    private async Task LoadTreeAsync(Commit commit)
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath)) return;
        
        try
        {
            var files = await _gitService.GetCommitFilesAsync(_repoViewModel.RepositoryPath, commit.Hash);
            var folderMap = new Dictionary<string, GitHalls.App.Models.GitTreeFileNode>();
            var rootNodes = new List<GitHalls.App.Models.GitTreeFileNode>();

            foreach (var file in files)
            {
                var parts = file.Path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                GitHalls.App.Models.GitTreeFileNode? currentParent = null;
                string currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    bool isLast = i == parts.Length - 1;
                    currentPath = string.IsNullOrEmpty(currentPath) ? parts[i] : currentPath + "/" + parts[i];

                    if (!folderMap.TryGetValue(currentPath, out var node))
                    {
                        node = new GitHalls.App.Models.GitTreeFileNode
                        {
                            Name = parts[i],
                            FullPath = currentPath,
                            IsFolder = !isLast,
                            File = isLast ? file : null
                        };
                        folderMap[currentPath] = node;

                        if (currentParent == null)
                        {
                            rootNodes.Add(node);
                        }
                        else
                        {
                            currentParent.Children.Add(node);
                        }
                    }

                    currentParent = node;
                }
            }

            if (_lastExpandedRow != null && _lastExpandedRow.Commit.Hash == commit.Hash)
            {
                var flattened = new List<GitHalls.App.Models.GitTreeFileNode>();
                void FlattenNode(GitHalls.App.Models.GitTreeFileNode node, int depth)
                {
                    node.Depth = depth;
                    flattened.Add(node);
                    foreach (var child in node.Children.OrderBy(c => !c.IsFolder).ThenBy(c => c.Name))
                    {
                        FlattenNode(child, depth + 1);
                    }
                }
                foreach (var root in rootNodes.OrderBy(c => !c.IsFolder).ThenBy(c => c.Name))
                {
                    FlattenNode(root, 0);
                }
                _lastExpandedRow.TreeFiles = flattened;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath)) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var commits = await _gitService.GetGraphCommitsAsync(_repoViewModel.RepositoryPath);
            var graphRows = GraphBuilder.Build(commits);
            Rows = new ObservableCollection<GraphRow>(graphRows);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task CheckoutCommitAsync(GraphRow row)
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath) || row == null) return;
        _repoViewModel.IsBusy = true;
        try
        {
            var localBranch = row.Commit.Refs.FirstOrDefault(r => !r.StartsWith("origin/") && !r.Contains("HEAD") && !r.StartsWith("tag:"));
            var target = localBranch ?? row.Commit.Hash;
            await _gitService.CheckoutAsync(_repoViewModel.RepositoryPath, target);
            await _repoViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _repoViewModel.IsBusy = false;
        }
    }

    public async Task MergeCommitAsync(GraphRow row)
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath) || row == null) return;
        _repoViewModel.IsBusy = true;
        try
        {
            var localBranch = row.Commit.Refs.FirstOrDefault(r => !r.StartsWith("origin/") && !r.Contains("HEAD") && !r.StartsWith("tag:"));
            var target = localBranch ?? row.Commit.Hash;
            await _gitService.MergeAsync(_repoViewModel.RepositoryPath, target);
            await _repoViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _repoViewModel.IsBusy = false;
        }
    }

    public async Task CreateBranchAtAsync(GraphRow row, string branchName)
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath) || row == null) return;
        _repoViewModel.IsBusy = true;
        try
        {
            await _gitService.CreateBranchAtAsync(_repoViewModel.RepositoryPath, branchName, row.Commit.Hash);
            await _repoViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _repoViewModel.IsBusy = false;
        }
    }

    public async Task DeleteBranchAsync(string branchName)
    {
        if (string.IsNullOrEmpty(_repoViewModel.RepositoryPath)) return;
        _repoViewModel.IsBusy = true;
        try
        {
            await _gitService.DeleteBranchAsync(_repoViewModel.RepositoryPath, branchName, force: true);
            await _repoViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _repoViewModel.IsBusy = false;
        }
    }
}
