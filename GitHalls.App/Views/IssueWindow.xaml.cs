using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GitHalls.App.Views;

/// <summary>
/// One issue in a window of its own, like Settings: the card on the board is
/// a summary, and reading a description or naming a branch wants room and a
/// place that stays open while the board is used.
///
/// This is the only place the two view models are held together. Jira knows
/// nothing about repositories and the repository knows nothing about issues —
/// the join lives here, in the screen that actually needs both.
/// </summary>
public sealed partial class IssueWindow : Window
{
    private readonly JiraViewModel _jira;
    private readonly RepositoryViewModel _repository;

    /// <summary>Cancelled when the window closes: a slow Jira must not touch a window that is gone.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    private JiraIssue _issue;

    /// <summary>The suggestion in the box, to tell an edited name from an untouched one.</summary>
    private string _suggestion = string.Empty;

    /// <summary>The key this window shows; the main window keeps one window per key.</summary>
    public string Key => _issue.Key;

    public IssueWindow(JiraViewModel jira, RepositoryViewModel repository, JiraIssue issue)
    {
        _jira = jira;
        _repository = repository;
        _issue = issue;

        InitializeComponent();
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 760));

        Closed += (_, _) => _lifetime.Cancel();

        Title = $"{issue.Key} — GitHalls";

        _suggestion = _jira.SuggestedBranchName(issue);
        BranchNameTextBox.Text = _suggestion;

        Apply(issue);
        UpdateRepositoryState();

        _ = LoadDetailAsync();
    }

    // MARK: - The issue

    /// <summary>Paints what is known. Called twice: with the card, then with the full issue.</summary>
    private void Apply(JiraIssue issue)
    {
        _issue = issue;

        TitleBarText.Text = issue.Key;
        KeyText.Text = issue.Key;
        SummaryText.Text = issue.Summary;

        StatusText.Text = issue.Status;
        StatusDot.Fill = new SolidColorBrush(issue.StatusCategory switch
        {
            "done" => Microsoft.UI.Colors.MediumSeaGreen,
            "indeterminate" => Microsoft.UI.Colors.SteelBlue,
            _ => Microsoft.UI.Colors.Gray
        });

        TypeText.Text = issue.Type;
        PriorityText.Text = issue.Priority ?? "—";
        AssigneeText.Text = issue.AssigneeName ?? "Unassigned";
        UpdateAssignButton();
        ReporterText.Text = issue.ReporterName ?? "—";
        CreatedText.Text = FormatDate(issue.Created);
        UpdatedText.Text = FormatDate(issue.Updated);

        LabelsList.ItemsSource = issue.Labels;
        LabelsList.Visibility = issue.Labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Null means "not fetched yet", and the ring says so; empty means Jira
        // has nothing, which is worth stating rather than leaving a blank card.
        if (issue.Description != null)
        {
            var empty = issue.Description.Length == 0;
            DescriptionText.Text = empty ? "This issue has no description." : issue.Description;
            DescriptionText.Foreground = (Brush)Application.Current.Resources[
                empty ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"];
        }
    }

    private async Task LoadDetailAsync()
    {
        SetLoadingDescription(true);
        DescriptionInfoBar.IsOpen = false;

        try
        {
            var detail = await _jira.FetchIssueAsync(_issue.Key, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;

            Apply(detail);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_lifetime.IsCancellationRequested) return;

            DescriptionInfoBar.Message = ex.Message;
            DescriptionInfoBar.IsOpen = true;
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested) SetLoadingDescription(false);
        }
    }

    private void SetLoadingDescription(bool loading)
    {
        DescriptionRing.IsActive = loading;
        DescriptionRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading && _issue.Description == null) DescriptionText.Text = string.Empty;
    }

    private static string FormatDate(DateTimeOffset date) =>
        date == default ? "—" : date.ToLocalTime().ToString("d MMM yyyy, HH:mm");

    private async void OpenInJira_Click(object sender, RoutedEventArgs e)
    {
        if (_jira.BrowseUrl(_issue) is not { } url) return;

        await Launcher.LaunchUriAsync(url);
    }

    // MARK: - Acting on the issue

    /// <summary>
    /// The board's copy of this issue changed. Only the fields a write can move
    /// are taken from it: that copy comes from the search, which never asks for
    /// a description, and this window has already paid for one.
    /// </summary>
    public void UpdateIssue(JiraIssue? fresh)
    {
        if (fresh == null || fresh.Key != _issue.Key) return;

        Apply(_issue with
        {
            Status = fresh.Status,
            StatusCategory = fresh.StatusCategory,
            AssigneeName = fresh.AssigneeName,
            AssigneeAccountId = fresh.AssigneeAccountId
        });
    }

    /// <summary>Re-reads what Jira is doing to this issue right now.</summary>
    public void UpdateActionState()
    {
        var busy = _jira.IsIssueBusy(_issue.Key);

        StatusButton.IsEnabled = !busy;
        AssignToMeButton.IsEnabled = !busy;
        UpdateAssignButton();
        UpdateRepositoryState();

        // Only this issue's news. A move on another card belongs on the board,
        // not in a window that has nothing to do with it.
        if (_jira.ActionIssueKey == _issue.Key && _jira.ActionMessage is { Length: > 0 } message)
        {
            ShowAction(message, _jira.ActionFailed);
        }
    }

    /// <summary>
    /// Hidden once we know the issue is already yours — which we may not know
    /// at first paint: the account id arrives with the first search, after this
    /// window may already be open.
    /// </summary>
    private void UpdateAssignButton()
    {
        var mine = _jira.MyAccountId;
        AssignToMeButton.Visibility = mine != null && _issue.AssigneeAccountId == mine
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ShowAction(string message, bool failed)
    {
        ActionInfoBar.Message = message;
        ActionInfoBar.Severity = failed ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        ActionInfoBar.IsOpen = true;
    }

    private void ActionInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ActionInfoBar.IsOpen = false;
        _jira.ClearActionMessage();
    }

    /// <summary>Fills the status menu with the moves Jira allows from where the issue stands.</summary>
    private async void StatusMenu_Opening(object? sender, object e)
    {
        StatusMenu.Items.Clear();
        StatusMenu.Items.Add(new MenuFlyoutItem { Text = "Loading moves\u2026", IsEnabled = false });

        IReadOnlyList<JiraTransition> transitions;
        try
        {
            transitions = await _jira.TransitionsForAsync(_issue, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested) ReplaceStatusMenu(new MenuFlyoutItem { Text = ex.Message, IsEnabled = false });
            return;
        }

        if (_lifetime.IsCancellationRequested || !StatusMenu.IsOpen) return;

        StatusMenu.Items.Clear();

        if (transitions.Count == 0)
        {
            StatusMenu.Items.Add(new MenuFlyoutItem { Text = "No moves available", IsEnabled = false });
            return;
        }

        foreach (var transition in transitions)
        {
            var item = new MenuFlyoutItem
            {
                Text = JiraWorkflow.Label(transition, transitions) + (transition.HasScreen ? "\u2026" : string.Empty)
            };

            if (transition.HasScreen)
            {
                ToolTipService.SetToolTip(item, "Jira may ask for more before this move completes.");
            }

            item.Click += (_, _) => _ = _jira.MoveIssueAsync(_issue, transition);
            StatusMenu.Items.Add(item);
        }
    }

    private void ReplaceStatusMenu(MenuFlyoutItemBase item)
    {
        StatusMenu.Items.Clear();
        StatusMenu.Items.Add(item);
    }

    private void AssignToMe_Click(object sender, RoutedEventArgs e) => _ = _jira.AssignToMeAsync(_issue);

    // MARK: - Branch

    /// <summary>Re-reads the repository. Called by the main window when it changes.</summary>
    public void UpdateRepositoryState()
    {
        var hasRepository = !string.IsNullOrEmpty(_repository.RepositoryPath);
        var hasName = BranchNameTextBox.Text.Trim().Length > 0;

        var canCreate = hasRepository && hasName && !_repository.IsBusy;
        CreateBranchButton.IsEnabled = canCreate;
        StartWorkButton.IsEnabled = canCreate && !_jira.IsIssueBusy(_issue.Key);
        ResetNameButton.IsEnabled = BranchNameTextBox.Text != _suggestion;

        TargetRepositoryText.Text = hasRepository
            ? $"Will be created in {FolderName(_repository.RepositoryPath!)}, from the branch checked out there now."
            : "Open a repository first — a branch needs somewhere to be created.";
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void BranchName_TextChanged(object sender, TextChangedEventArgs e) => UpdateRepositoryState();

    private void BranchName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || !CreateBranchButton.IsEnabled) return;

        e.Handled = true;
        CreateBranch();
    }

    private void ResetName_Click(object sender, RoutedEventArgs e)
    {
        BranchNameTextBox.Text = _suggestion;
    }

    private void CreateBranch_Click(object sender, RoutedEventArgs e) => CreateBranch();

    private void CreateBranch() => _ = _repository.CreateBranchAsync(BranchNameTextBox.Text.Trim());

    /// <summary>
    /// Branch, then assign, then move — in that order on purpose. The local
    /// step is the one most likely to fail (no repository open, a name already
    /// taken), and claiming an issue for work that then has nowhere to happen
    /// is the worse half to get wrong.
    /// </summary>
    private async void StartWork_Click(object sender, RoutedEventArgs e)
    {
        // RunGitAsync returns in silence while another git command runs, and a
        // disabled button can be stale across an await.
        if (_repository.IsBusy || _jira.IsIssueBusy(_issue.Key)) return;

        StartWorkButton.IsEnabled = false;
        ActionInfoBar.IsOpen = false;

        try
        {
            await _repository.CreateBranchAsync(BranchNameTextBox.Text.Trim());
            if (_repository.HasError)
            {
                ShowAction(_repository.ErrorMessage ?? "The branch could not be created.", failed: true);
                return;
            }

            var outcome = await _jira.StartWorkJiraAsync(_issue);

            // Null means Jira refused and already said why; anything else is a
            // success, whole or partial, and partial is worth saying out loud.
            if (outcome == null)
            {
                ShowAction(_jira.ActionMessage ?? "Jira refused the change.", failed: true);
                return;
            }

            // Read the status back rather than trusting _issue: it is only
            // current because the move's board update happens to have reached
            // this window first, and that is not a thing to depend on.
            var status = _jira.FindIssue(_issue.Key)?.Status ?? _issue.Status;

            ShowAction(outcome switch
            {
                JiraStartWork.Move => $"Branch created, assigned to you, moved to {status}.",
                JiraStartWork.AlreadyInProgress => "Branch created and assigned to you. It was already in progress.",
                _ => "Branch created and assigned to you. This workflow has no in-progress move — change the status in Jira."
            }, failed: false);
        }
        finally
        {
            UpdateRepositoryState();
        }
    }
}
