using GitHalls.App.Services;
using GitHalls.App.ViewModels;
using GitHalls.App.Views;
using GitHalls.Core.Git;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App;

public sealed partial class MainWindow : Window
{
    private const double DefaultSidebarMinWidth = 220;

    private readonly GitService _gitService = new();
    private readonly PlatformActions _platformActions = new();

    /// <summary>Sidebar width to restore when it is expanded again.</summary>
    private double _restoreSidebarWidth = 280;

    public RepositoryViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        SetTitleBar(AppTitleBar);

        ViewModel = new RepositoryViewModel(_gitService, new SettingsStore());

        Activated += MainWindow_Activated;
        Closed += (_, _) => ViewModel.Dispose();

        MainSelectorBar.SelectedItem = ChangesTab;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _ = ViewModel.InitializeAsync();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;

        // Same behaviour as the Swift app on didBecomeActive: pick up what
        // happened outside the app, including on the remote. Rate-limited
        // inside the view model so alt-tabbing doesn't hit the network.
        _ = ViewModel.FetchOnActivationAsync();
    }

    private void MainSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var suppressInfo = new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo();

        // Sidebar and detail pane move together: Changes pairs with the file
        // diff, History with the commit detail.
        if (sender.SelectedItem == HistoryTab)
        {
            SidebarFrame.Navigate(typeof(HistorySidebarPage), ViewModel, suppressInfo);
            ContentFrame.Navigate(typeof(CommitDetailPage), ViewModel, suppressInfo);
            (ContentFrame.Content as CommitDetailPage)?.Update();
        }
        else
        {
            SidebarFrame.Navigate(typeof(ChangesSidebarPage), ViewModel, suppressInfo);
            ContentFrame.Navigate(typeof(DiffPage), null, suppressInfo);
            (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff);
        }
    }

    // MARK: - Repository

    private void RepositoryFlyout_Opening(object? sender, object e)
    {
        // Drop the previously listed recents, keeping the two fixed entries and
        // the separator.
        const int fixedItemCount = 3;
        while (RepositoryFlyout.Items.Count > fixedItemCount)
        {
            RepositoryFlyout.Items.RemoveAt(RepositoryFlyout.Items.Count - 1);
        }

        var recents = ViewModel.RecentRepositories
            .Where(p => !string.Equals(p, ViewModel.RepositoryPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        RecentSeparator.Visibility = recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var path in recents)
        {
            var item = new MenuFlyoutItem { Text = FolderName(path) };
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => ViewModel.RepositoryPath = path;
            RepositoryFlyout.Items.Add(item);
        }
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private async void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        var folder = await _platformActions.PickFolderAsync(this);
        if (folder != null) ViewModel.RepositoryPath = folder;
    }

    private async void CloneRepo_Click(object sender, RoutedEventArgs e)
    {
        var urlTextBox = new TextBox { PlaceholderText = "https://github.com/user/repo.git", Width = 400 };
        var folderButton = new Button { Content = "Select Destination Folder..." };
        var selectedFolderText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
        };
        string? destinationPath = null;

        folderButton.Click += async (_, _) =>
        {
            destinationPath = await _platformActions.PickFolderAsync(this);
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
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(urlTextBox.Text) || string.IsNullOrEmpty(destinationPath)) return;

        ViewModel.IsBusy = true;
        try
        {
            // CloneAsync returns the directory git actually created, so the app
            // opens that instead of a path assembled from the URL.
            var clonedPath = await _gitService.CloneAsync(destinationPath, urlTextBox.Text.Trim());
            ViewModel.RepositoryPath = clonedPath;
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

    // MARK: - Branches

    private async void MergeBranch_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.RepositoryPath) || ViewModel.CurrentBranch == null) return;

        var branchComboBox = new ComboBox
        {
            ItemsSource = ViewModel.Branches.Where(b => b.Name != ViewModel.CurrentBranch.Name).ToList(),
            DisplayMemberPath = "Name",
            Width = 300,
            PlaceholderText = "Select branch to merge..."
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            // Named explicitly, like the Swift merge sheet: a merge changes the
            // branch you are on, which is easy to forget in the moment.
            Text = $"Merging into '{ViewModel.CurrentBranch.Name}' — that is the branch that will change.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(branchComboBox);

        var dialog = new ContentDialog
        {
            Title = "Merge Branch",
            Content = panel,
            PrimaryButtonText = "Merge",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && branchComboBox.SelectedItem is Branch targetBranch)
        {
            await ViewModel.MergeBranchAsync(targetBranch);
        }
    }

    /// <summary>
    /// Builds the branch list on open: local branches first, then remotes.
    ///
    /// This replaced a ComboBox, which had to be defended against its own
    /// SelectionChanged — repopulating the list on every refresh nulled and
    /// reassigned SelectedItem, which read as "the user picked a branch" and
    /// fired a checkout. A menu has no selection state to clobber.
    /// </summary>
    private void BranchFlyout_Opening(object? sender, object e)
    {
        BranchFlyout.Items.Clear();

        var current = ViewModel.CurrentBranch?.Name;
        var locals = ViewModel.Branches.Where(b => !b.IsRemote).ToList();
        var remotes = ViewModel.Branches.Where(b => b.IsRemote).ToList();

        if (locals.Count == 0 && remotes.Count == 0)
        {
            BranchFlyout.Items.Add(new MenuFlyoutItem { Text = "No branches", IsEnabled = false });
            return;
        }

        foreach (var branch in locals)
        {
            BranchFlyout.Items.Add(BuildBranchItem(branch, isCurrent: branch.Name == current));
        }

        if (remotes.Count > 0)
        {
            if (locals.Count > 0) BranchFlyout.Items.Add(new MenuFlyoutSeparator());
            foreach (var branch in remotes)
            {
                BranchFlyout.Items.Add(BuildBranchItem(branch, isCurrent: false));
            }
        }
    }

    private MenuFlyoutItem BuildBranchItem(Branch branch, bool isCurrent)
    {
        var item = new MenuFlyoutItem
        {
            Text = branch.Name,
            IsEnabled = !isCurrent
        };

        if (isCurrent)
        {
            item.Icon = new FontIcon { Glyph = "\uE73E" }; // checkmark
        }

        item.Click += async (_, _) => await ViewModel.CheckoutBranchAsync(branch);
        return item;
    }

    // MARK: - Quick actions

    private void RevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RepositoryPath)) _platformActions.RevealInExplorer(ViewModel.RepositoryPath);
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RepositoryPath)) _platformActions.OpenTerminal(ViewModel.RepositoryPath);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Collapses the sidebar to zero width and back, remembering the width it had.</summary>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarColumn.ActualWidth > 0)
        {
            _restoreSidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            Sidebar.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarColumn.MinWidth = DefaultSidebarMinWidth;
            SidebarColumn.Width = new GridLength(_restoreSidebarWidth);
            Sidebar.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
        }
    }

    private void ErrorBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();

    // MARK: - Diff

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.RepositoryPath))
        {
            var hasRepo = !string.IsNullOrEmpty(ViewModel.RepositoryPath);
            var name = hasRepo ? FolderName(ViewModel.RepositoryPath!) : null;

            TitleBarText.Text = name == null ? "GitHalls" : $"GitHalls — {name}";
            RepositoryButtonText.Text = name ?? "Open Repository";
            ToolTipService.SetToolTip(RepositoryButton, ViewModel.RepositoryPath ?? "No repository open");
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(RepositoryViewModel.CurrentBranch):
                BranchButtonText.Text = ViewModel.CurrentBranch?.Name ?? "Branch";
                ToolTipService.SetToolTip(BranchButton, ViewModel.CurrentBranch?.Name ?? "No branch");
                break;

            case nameof(RepositoryViewModel.CurrentDiff):
                // Only if that pane is the one on screen — the History tab owns
                // the frame while it is selected.
                (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff);
                break;

            case nameof(RepositoryViewModel.SelectedCommit):
            case nameof(RepositoryViewModel.SelectedCommitDetail):
            case nameof(RepositoryViewModel.IsLoadingCommitDetail):
                (ContentFrame.Content as CommitDetailPage)?.Update();
                break;
        }
    }
}
