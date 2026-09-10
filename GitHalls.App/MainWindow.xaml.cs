using GitHalls.App.Controls;
using GitHalls.App.Helpers;
using GitHalls.App.Services;
using GitHalls.App.ViewModels;
using GitHalls.App.Views;
using GitHalls.Core.Git;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GitHalls.App;

public sealed partial class MainWindow : Window
{
    private const double DefaultSidebarMinWidth = 340;

    private readonly GitService _gitService = new();
    private readonly PlatformActions _platformActions = new();

    /// <summary>One store for the whole app: several screens write to it.</summary>
    private readonly SettingsStore _settingsStore = new();

    private readonly JiraAccountStore _jiraAccount;

    /// <summary>Open settings window, if any — a second one would fight the first.</summary>
    private SettingsWindow? _settingsWindow;

    /// <summary>Open issue windows by key: clicking a card twice brings the window forward, not a twin.</summary>
    private readonly Dictionary<string, IssueWindow> _issueWindows = new(StringComparer.Ordinal);

    /// <summary>Sidebar width to restore when it is expanded again.</summary>
    private double _restoreSidebarWidth = 340;

    public RepositoryViewModel ViewModel { get; }

    /// <summary>Jira state, separate from the repository's. They meet only in the issue window.</summary>
    public JiraViewModel JiraViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        this.ConfigureCustomTitleBar(AppTitleBar);

        ViewModel = new RepositoryViewModel(_gitService, _settingsStore);

        _jiraAccount = new JiraAccountStore(_settingsStore);
        JiraViewModel = new JiraViewModel(_jiraAccount, _settingsStore);
        JiraViewModel.PropertyChanged += JiraViewModel_PropertyChanged;

        Activated += MainWindow_Activated;
        Closed += (_, _) => ViewModel.Dispose();

        MainSelectorBar.SelectedItem = ChangesTab;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        _ = StartAsync();
    }

    /// <summary>
    /// Settings arrive asynchronously, and the Jira account is read from them —
    /// so anything that depends on "is an account connected" has to wait for
    /// this, not for the constructor.
    /// </summary>
    private async Task StartAsync()
    {
        await ViewModel.InitializeAsync();

        JiraViewModel.LoadSavedQueries();
        JiraViewModel.NotifyAccountChanged();

        // Only if the board is what's on screen: opening the app on Changes
        // should not call Jira at all.
        if (ContentFrame.Content is KanbanBoardPage board)
        {
            (SidebarFrame.Content as KanbanSidebarPage)?.Update();
            board.Update();
            if (JiraViewModel.IsConfigured && !JiraViewModel.HasSearched) await JiraViewModel.RefreshAsync();
        }
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
        // diff, History with the commit detail, Kanban's queries with its board.
        if (sender.SelectedItem == KanbanTab)
        {
            SidebarFrame.Navigate(typeof(KanbanSidebarPage), JiraViewModel, suppressInfo);
            if (SidebarFrame.Content is KanbanSidebarPage kanban) kanban.SettingsRequested += (_, _) => OpenSettings();

            ContentFrame.Navigate(typeof(KanbanBoardPage), JiraViewModel, suppressInfo);
            if (ContentFrame.Content is KanbanBoardPage board)
            {
                board.SettingsRequested += (_, _) => OpenSettings();
                board.IssueOpened += (_, issue) => OpenIssue(issue);
            }
        }
        else if (sender.SelectedItem == GitgraphTab)
        {
            // Empty sidebar for graph, or we could add a list of branches later.
            SidebarFrame.Content = null; 
            ContentFrame.Navigate(typeof(GitgraphPage), ViewModel, suppressInfo);
            if (ContentFrame.Content is GitgraphPage page)
            {
                _ = page.ViewModel.LoadAsync();
            }
        }
        else if (sender.SelectedItem == HistoryTab)
        {
            SidebarFrame.Navigate(typeof(HistorySidebarPage), ViewModel, suppressInfo);
            ContentFrame.Navigate(typeof(CommitDetailPage), ViewModel, suppressInfo);
            (ContentFrame.Content as CommitDetailPage)?.Update();
        }
        else
        {
            SidebarFrame.Navigate(typeof(ChangesSidebarPage), ViewModel, suppressInfo);
            ContentFrame.Navigate(typeof(DiffPage), null, suppressInfo);
            (ContentFrame.Content as DiffPage)?.SetViewModel(ViewModel);
            (ContentFrame.Content as DiffPage)?.SetSideBySide(ViewModel.IsSideBySideDiff);
            (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff, ViewModel.CurrentDiffPreview);
        }
    }

    // MARK: - Jira

    private void JiraViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModels.JiraViewModel.SelectedQuery):
            case nameof(ViewModels.JiraViewModel.Queries):
                // Both panes: the sidebar moves its highlight, the board its title.
                (SidebarFrame.Content as KanbanSidebarPage)?.Update();
                (ContentFrame.Content as KanbanBoardPage)?.Update();
                break;

            case nameof(ViewModels.JiraViewModel.Columns):
                (ContentFrame.Content as KanbanBoardPage)?.Update();
                // A write moves the issue for the board and for whoever has it
                // open: until now issue windows only heard about the repository.
                foreach (var issueWindow in _issueWindows.Values)
                {
                    issueWindow.UpdateIssue(JiraViewModel.FindIssue(issueWindow.Key));
                }
                break;

            case nameof(ViewModels.JiraViewModel.IsLoading):
            case nameof(ViewModels.JiraViewModel.ErrorMessage):
            case nameof(ViewModels.JiraViewModel.HasSearched):
                (ContentFrame.Content as KanbanBoardPage)?.Update();
                break;

            case nameof(ViewModels.JiraViewModel.BusyIssueKeys):
                // Its own case, not a ride on Columns: Update() skips the
                // rebuild when the columns are unchanged, which is exactly the
                // case here.
                (ContentFrame.Content as KanbanBoardPage)?.UpdateBusyCards();
                foreach (var issueWindow in _issueWindows.Values) issueWindow.UpdateActionState();
                break;

            case nameof(ViewModels.JiraViewModel.MyAccountId):
                foreach (var issueWindow in _issueWindows.Values) issueWindow.UpdateActionState();
                break;

            case nameof(ViewModels.JiraViewModel.ActionMessage):
            case nameof(ViewModels.JiraViewModel.ActionFailed):
                (ContentFrame.Content as KanbanBoardPage)?.Update();
                foreach (var issueWindow in _issueWindows.Values) issueWindow.UpdateActionState();
                break;

            case nameof(ViewModels.JiraViewModel.IsConfigured):
                (SidebarFrame.Content as KanbanSidebarPage)?.Update();
                (ContentFrame.Content as KanbanBoardPage)?.Update();
                break;
        }
    }

    /// <summary>One window per issue, like Settings has one window: a second click brings it forward.</summary>
    private void OpenIssue(Core.Jira.JiraIssue issue)
    {
        if (_issueWindows.TryGetValue(issue.Key, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new IssueWindow(JiraViewModel, ViewModel, issue);
        _issueWindows[issue.Key] = window;
        window.Closed += (_, _) => _issueWindows.Remove(issue.Key);
        window.Activate();
    }

    // MARK: - Settings

    private void StashAndPull_Click(object sender, RoutedEventArgs e) => _ = ViewModel.PullWithStashAsync();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_jiraAccount, ViewModel);
        _settingsWindow = window;

        window.AccountChanged += (_, _) =>
        {
            JiraViewModel.NotifyAccountChanged();

            // Newly connected and the board on screen with nothing on it: fill it.
            if (ContentFrame.Content is KanbanBoardPage && JiraViewModel.IsConfigured && !JiraViewModel.HasSearched)
            {
                _ = JiraViewModel.RefreshAsync();
            }
        };

        window.Closed += (_, _) => _settingsWindow = null;
        window.Activate();
    }

    private void IdentityFlyout_Opening(object? sender, object e)
    {
        IdentitySwitcherControl.ActionCompleted -= IdentitySwitcher_ActionCompleted;
        IdentitySwitcherControl.ActionCompleted += IdentitySwitcher_ActionCompleted;
        IdentitySwitcherControl.ManageRequested -= IdentitySwitcher_ManageRequested;
        IdentitySwitcherControl.ManageRequested += IdentitySwitcher_ManageRequested;
        IdentitySwitcherControl.Load(ViewModel);
    }

    private void IdentitySwitcher_ActionCompleted(object? sender, EventArgs e) => IdentityFlyout.Hide();

    private void IdentitySwitcher_ManageRequested(object? sender, EventArgs e) => OpenSettings();

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

        var sheet = new MergeSheet();

        var dialog = new ContentDialog
        {
            Title = $"Merge into \"{ViewModel.CurrentBranch.Name}\"",
            Content = sheet,
            PrimaryButtonText = "Merge",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = Content.XamlRoot
        };

        // Nothing to merge until a branch is picked.
        sheet.SelectionValidChanged += (_, valid) => dialog.IsPrimaryButtonEnabled = valid;
        sheet.Load(ViewModel);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && sheet.SelectedBranch is { } targetBranch)
        {
            await ViewModel.MergeBranchAsync(targetBranch);
        }
    }

    /// <summary>
    /// Opens a pull request for the current branch: the sheet collects title,
    /// description and base, the view model pushes and then creates it, and the
    /// URL that comes back is opened here.
    /// </summary>
    private async void CreatePullRequest_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.RepositoryPath) || ViewModel.CurrentBranch == null) return;

        var sheet = new CreatePullRequestSheet();

        var dialog = new ContentDialog
        {
            Title = "Create Pull Request",
            Content = sheet,
            PrimaryButtonText = "Create Pull Request",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = Content.XamlRoot
        };

        // A pull request with no title is not one GitHub will take.
        sheet.IsValidChanged += (_, valid) => dialog.IsPrimaryButtonEnabled = valid;
        await sheet.LoadAsync(ViewModel);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var url = await ViewModel.CreatePullRequestAsync(sheet.Title, sheet.Description, sheet.BaseBranch);
        if (!string.IsNullOrEmpty(url)) _platformActions.OpenUrl(url);
    }

    /// <summary>
    /// Hands the picker the current state each time it opens, and closes the
    /// flyout as soon as it acts.
    ///
    /// This replaced a ComboBox, which had to be defended against its own
    /// SelectionChanged — repopulating the list on every refresh nulled and
    /// reassigned SelectedItem, which read as "the user picked a branch" and
    /// fired a checkout. A flyout has no selection state to clobber.
    /// </summary>
    private void BranchFlyout_Opening(object? sender, object e)
    {
        BranchSwitcherControl.ActionCompleted -= BranchSwitcher_ActionCompleted;
        BranchSwitcherControl.ActionCompleted += BranchSwitcher_ActionCompleted;
        BranchSwitcherControl.Load(ViewModel);
    }

    private void BranchSwitcher_ActionCompleted(object? sender, EventArgs e) => BranchFlyout.Hide();

    // MARK: - Quick actions

    private void RevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RepositoryPath)) _platformActions.RevealInExplorer(ViewModel.RepositoryPath);
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.RepositoryPath)) _platformActions.OpenTerminal(ViewModel.RepositoryPath);
    }

    // Shortcuts the removed MenuBar used to declare.
    private void ToggleSidebarAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleSidebar_Click(sender, new RoutedEventArgs());
    }

    private void CreatePullRequestAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CreatePullRequest_Click(sender, new RoutedEventArgs());
    }

    private void OpenRepoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenRepo_Click(sender, new RoutedEventArgs());
    }

    /// <summary>Collapses the sidebar to zero width and back, remembering the width it had.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sidebarAnimTimer;
    private double _sidebarAnimTarget;
    private double _sidebarAnimCurrent;

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarColumn.ActualWidth > 0)
        {
            _restoreSidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.MinWidth = 0;
            _sidebarAnimTarget = 0;
            _sidebarAnimCurrent = SidebarColumn.ActualWidth;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            Sidebar.Visibility = Visibility.Visible;
            SidebarColumn.MinWidth = 0; // Keep 0 during animation
            _sidebarAnimTarget = _restoreSidebarWidth;
            _sidebarAnimCurrent = SidebarColumn.ActualWidth;
        }

        if (_sidebarAnimTimer == null)
        {
            _sidebarAnimTimer = DispatcherQueue.CreateTimer();
            _sidebarAnimTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
            _sidebarAnimTimer.Tick += (s, args) =>
            {
                var diff = _sidebarAnimTarget - _sidebarAnimCurrent;
                if (Math.Abs(diff) < 1.0)
                {
                    _sidebarAnimCurrent = _sidebarAnimTarget;
                    SidebarColumn.Width = new GridLength(_sidebarAnimCurrent);
                    _sidebarAnimTimer.Stop();
                    
                    if (_sidebarAnimTarget == 0)
                    {
                        Sidebar.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        SidebarColumn.MinWidth = DefaultSidebarMinWidth;
                        SidebarSplitter.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    // Ease out
                    _sidebarAnimCurrent += diff * 0.3;
                    SidebarColumn.Width = new GridLength(_sidebarAnimCurrent);
                }
            };
        }
        
        _sidebarAnimTimer.Start();
    }

    private void SideBySideToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsSideBySideDiff = SideBySideToggle.IsChecked == true;
    }

    private void ErrorBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();

    // MARK: - Diff

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.RepositoryPath))
        {
            var hasRepo = !string.IsNullOrEmpty(ViewModel.RepositoryPath);
            var name = hasRepo ? FolderName(ViewModel.RepositoryPath!) : null;

            TitleBarText.Text = name == null ? "GitHalls" : $"GitHalls • {name}";
            RepositoryButtonText.Text = name ?? "Open Repository";
            ToolTipService.SetToolTip(RepositoryButton, ViewModel.RepositoryPath ?? "No repository open");

            // Every issue window names the repository a branch would be created in.
            foreach (var issueWindow in _issueWindows.Values) issueWindow.UpdateRepositoryState();
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(RepositoryViewModel.IsBusy):
                // "Create Branch" is disabled while git is working.
                foreach (var issueWindow in _issueWindows.Values) issueWindow.UpdateRepositoryState();
                break;

            case nameof(RepositoryViewModel.CurrentAuthor):
                var author = ViewModel.CurrentAuthor;
                IdentityButtonText.Text = author == null ? "No identity" : author.Name;
                ToolTipService.SetToolTip(IdentityButton, author?.ToString() ?? "git has no name or email configured here");
                break;

            case nameof(RepositoryViewModel.CurrentBranch):
                BranchButtonText.Text = ViewModel.CurrentBranch?.Name ?? "Branch";
                ToolTipService.SetToolTip(BranchButton, ViewModel.CurrentBranch?.Name ?? "No branch");
                break;

            case nameof(RepositoryViewModel.CurrentDiff):
            case nameof(RepositoryViewModel.WorkingTreeDiff):
            case nameof(RepositoryViewModel.IndexDiff):
            case nameof(RepositoryViewModel.DiffSideSelection):
                // Only if that pane is the one on screen — the History tab owns
                // the frame while it is selected.
                //
                // The two sides come through here as well: the toggle above the
                // diff shows what each of them holds, so it has to be repainted
                // when either changes and not only when the shown one does.
                (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff, ViewModel.CurrentDiffPreview);
                break;

            case nameof(RepositoryViewModel.IsSideBySideDiff):
                // Also reaches the toggle, which starts out reflecting a setting
                // that is only read once the window is already up.
                SideBySideToggle.IsChecked = ViewModel.IsSideBySideDiff;
                (ContentFrame.Content as DiffPage)?.SetSideBySide(ViewModel.IsSideBySideDiff);
                (ContentFrame.Content as CommitDetailPage)?.Update();
                break;

            case nameof(RepositoryViewModel.SelectedCommit):
            case nameof(RepositoryViewModel.IsLoadingCommitFiles):
            case nameof(RepositoryViewModel.SelectedCommitFile):
            case nameof(RepositoryViewModel.Readme):
                // Repainted through the same call as the diff: the README only
                // shows where the diff is absent, and one path decides both.
                (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff, ViewModel.CurrentDiffPreview);
                (ContentFrame.Content as CommitDetailPage)?.Update();
                break;

            case nameof(RepositoryViewModel.PullBlockedByLocalChanges):
                StashAndPullButton.Visibility = ViewModel.PullBlockedByLocalChanges
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                break;

            case nameof(RepositoryViewModel.CurrentDiffPreview):
                (ContentFrame.Content as DiffPage)?.UpdateDiff(ViewModel.CurrentDiff, ViewModel.CurrentDiffPreview);
                break;

            case nameof(RepositoryViewModel.CommitFilePreview):
            case nameof(RepositoryViewModel.CommitFileDiff):
            case nameof(RepositoryViewModel.IsLoadingCommitFileDiff):
                (ContentFrame.Content as CommitDetailPage)?.Update();
                break;
        }
    }

    public Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
}
