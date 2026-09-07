using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace GitHalls.App.Views;

/// <summary>The two view models this page needs, since it is where they meet.</summary>
public sealed record IssueDetailParameter(JiraViewModel Jira, RepositoryViewModel Repository);

/// <summary>
/// One issue, and the single action that ties Jira to git: start a branch for it.
///
/// This is the only place the two view models are held together. Jira knows
/// nothing about repositories and the repository knows nothing about issues —
/// the join lives here, in the screen that actually needs both.
/// </summary>
public sealed partial class IssueDetailPage : Page
{
    private JiraViewModel _jira = null!;
    private RepositoryViewModel _repository = null!;

    /// <summary>The issue whose suggestion is currently in the box.</summary>
    private string? _suggestedFor;

    /// <summary>The suggestion itself, to tell an edited name from an untouched one.</summary>
    private string _suggestion = string.Empty;

    public IssueDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not IssueDetailParameter parameter) return;

        _jira = parameter.Jira;
        _repository = parameter.Repository;
        Update();
    }

    /// <summary>Re-reads both view models. Called by the window on every relevant change.</summary>
    public void Update()
    {
        if (_jira == null) return;

        var issue = _jira.SelectedIssue;
        if (issue == null)
        {
            PlaceholderPanel.Visibility = Visibility.Visible;
            DetailScroller.Visibility = Visibility.Collapsed;
            return;
        }

        PlaceholderPanel.Visibility = Visibility.Collapsed;
        DetailScroller.Visibility = Visibility.Visible;

        KeyText.Text = issue.Key;
        SummaryText.Text = issue.Summary;
        StatusText.Text = issue.Status;
        TypeText.Text = issue.Type;
        UpdatedText.Text = issue.Updated == default
            ? "no date"
            : issue.Updated.ToLocalTime().ToString("d MMM yyyy, HH:mm");

        var hasPriority = !string.IsNullOrWhiteSpace(issue.Priority);
        PriorityText.Text = issue.Priority ?? string.Empty;
        PriorityText.Visibility = hasPriority ? Visibility.Visible : Visibility.Collapsed;
        PrioritySeparator.Visibility = hasPriority ? Visibility.Visible : Visibility.Collapsed;

        // A new issue gets its suggestion; a name the user typed for the issue
        // they are still looking at is left alone.
        if (_suggestedFor != issue.Key)
        {
            _suggestedFor = issue.Key;
            _suggestion = _jira.SuggestedBranchName(issue);
            BranchNameTextBox.Text = _suggestion;
        }

        UpdateBranchState();
    }

    private void UpdateBranchState()
    {
        var hasRepository = !string.IsNullOrEmpty(_repository.RepositoryPath);
        var hasName = BranchNameTextBox.Text.Trim().Length > 0;

        CreateBranchButton.IsEnabled = hasRepository && hasName && !_repository.IsBusy;
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

    private void BranchName_TextChanged(object sender, TextChangedEventArgs e) => UpdateBranchState();

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

    private async void OpenInJira_Click(object sender, RoutedEventArgs e)
    {
        if (_jira.SelectedIssue is not { } issue) return;
        if (_jira.BrowseUrl(issue) is not { } url) return;

        await Launcher.LaunchUriAsync(url);
    }
}
