using Microsoft.UI.Xaml;
using GitHalls.App.ViewModels;
using GitHalls.Core.Git;
using GitHalls.App.Views;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App;

public sealed partial class MainWindow : Window
{
    public RepositoryViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        SetTitleBar(AppTitleBar);

        // Normally provided by DI in App.xaml.cs, we will keep it simple here.
        ViewModel = new RepositoryViewModel(new GitService());

        // When the window is activated, we trigger a refresh (fetch on focus equivalent)
        this.Activated += MainWindow_Activated;

        // Default navigation
        MainSelectorBar.SelectedItem = ChangesTab;
        
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _ = ViewModel.RefreshAsync();
        }
    }

    private void MainSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var suppressInfo = new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo();
        if (sender.SelectedItem == ChangesTab)
        {
            SidebarFrame.Navigate(typeof(ChangesSidebarPage), ViewModel, suppressInfo);
        }
        else if (sender.SelectedItem == HistoryTab)
        {
            SidebarFrame.Navigate(typeof(HistorySidebarPage), ViewModel, suppressInfo);
        }
    }

    private async void CloneRepo_Click(object sender, RoutedEventArgs e)
    {
        var urlTextBox = new TextBox { PlaceholderText = "https://github.com/user/repo.git", Width = 400 };
        var folderButton = new Button { Content = "Select Destination Folder..." };
        var selectedFolderText = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) };
        string destinationPath = null;

        folderButton.Click += async (s, args) =>
        {
            var platform = new GitHalls.App.Services.PlatformActions();
            destinationPath = await platform.PickFolderAsync(this);
            if (destinationPath != null) selectedFolderText.Text = destinationPath;
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Repository URL:" });
        panel.Children.Add(urlTextBox);
        panel.Children.Add(folderButton);
        panel.Children.Add(selectedFolderText);

        var dialog = new ContentDialog
        {
            Title = "Clone Repository",
            Content = panel,
            PrimaryButtonText = "Clone",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(urlTextBox.Text) && !string.IsNullOrEmpty(destinationPath))
        {
            ViewModel.IsBusy = true;
            try
            {
                var gitService = new GitService();
                await gitService.CloneAsync(destinationPath, urlTextBox.Text);
                
                // Get the repo name from the URL to open it
                var repoName = urlTextBox.Text.Split('/').Last().Replace(".git", "");
                var fullPath = System.IO.Path.Combine(destinationPath, repoName);
                
                ViewModel.RepositoryPath = fullPath;
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = ex.Message;
            }
            finally
            {
                ViewModel.IsBusy = false;
            }
        }
    }

    private async void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        var platform = new GitHalls.App.Services.PlatformActions();
        var folder = await platform.PickFolderAsync(this);
        if (folder != null)
        {
            ViewModel.RepositoryPath = folder;
        }
    }

    private async void MergeBranch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.RepositoryPath) || ViewModel.CurrentBranch == null) return;

        var branchComboBox = new ComboBox
        {
            ItemsSource = ViewModel.Branches,
            DisplayMemberPath = "Name",
            Width = 300,
            PlaceholderText = "Select branch to merge..."
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = $"Merge into '{ViewModel.CurrentBranch.Name}':" });
        panel.Children.Add(branchComboBox);

        var dialog = new ContentDialog
        {
            Title = "Merge Branch",
            Content = panel,
            PrimaryButtonText = "Merge",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && branchComboBox.SelectedItem is GitHalls.Core.Models.Branch targetBranch)
        {
            ViewModel.IsBusy = true;
            try
            {
                var gitService = new GitService();
                await gitService.MergeAsync(ViewModel.RepositoryPath, targetBranch.Name);
                await ViewModel.RefreshAsync();
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = $"Merge failed: {ex.Message}";
            }
            finally
            {
                ViewModel.IsBusy = false;
            }
        }
    }

    private async void BranchSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is GitHalls.Core.Models.Branch branch)
        {
            if (branch != ViewModel.CurrentBranch)
            {
                await ViewModel.CheckoutBranchAsync(branch);
            }
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.CurrentDiff))
        {
            if (ContentFrame.Content is DiffPage diffPage)
            {
                diffPage.UpdateDiff(ViewModel.CurrentDiff);
            }
            else
            {
                ContentFrame.Navigate(typeof(DiffPage));
                if (ContentFrame.Content is DiffPage newDiffPage)
                {
                    newDiffPage.UpdateDiff(ViewModel.CurrentDiff);
                }
            }
        }
    }
}
