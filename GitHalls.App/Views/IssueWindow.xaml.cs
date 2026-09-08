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

    // MARK: - Branch

    /// <summary>Re-reads the repository. Called by the main window when it changes.</summary>
    public void UpdateRepositoryState()
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
}
