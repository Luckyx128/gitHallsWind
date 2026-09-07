using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using Windows.System;
using Windows.UI.Text;

namespace GitHalls.App.Views;

/// <summary>
/// One row of the board: either a status header or an issue.
///
/// Headers travel in the same collection as the issues, the same way the branch
/// picker builds its sections — one virtualized list instead of a list per
/// group, and no selection state spread across several controls.
/// </summary>
public sealed class KanbanRow
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Trailing { get; set; } = string.Empty;
    public bool IsHeader { get; set; }
    public string Category { get; set; } = string.Empty;

    /// <summary>The issue this row stands for. Null for a header.</summary>
    public JiraIssue? Issue { get; set; }

    public string ToolTip => Issue == null ? Title : $"{Issue.Key} — {Issue.Summary}";

    public double FontSize => IsHeader ? 12 : 14;
    public FontWeight FontWeight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility DotVisibility => IsHeader ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SubtitleVisibility => Subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Brush Foreground => (Brush)Application.Current.Resources[
        IsHeader ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"];

    /// <summary>Jira's three categories, in the colours the app already uses for state.</summary>
    public Brush CategoryBrush => new SolidColorBrush(Category switch
    {
        "done" => Microsoft.UI.Colors.MediumSeaGreen,
        "indeterminate" => Microsoft.UI.Colors.SteelBlue,
        _ => Microsoft.UI.Colors.Gray
    });
}

public sealed partial class KanbanSidebarPage : Page
{
    public JiraViewModel ViewModel { get; private set; } = null!;

    private ObservableCollection<KanbanRow> Rows { get; } = new();

    /// <summary>Set while the list is rebuilt, so restoring the selection doesn't read as a click.</summary>
    private bool _rebuilding;

    public KanbanSidebarPage()
    {
        InitializeComponent();
        IssueList.ItemsSource = Rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not JiraViewModel viewModel) return;

        ViewModel = viewModel;
        DataContext = viewModel;
        JqlTextBox.Text = viewModel.Jql;

        Update();

        // First visit with an account already connected: fill the board without
        // making the user press anything.
        if (ViewModel.IsConfigured && !ViewModel.HasSearched && !ViewModel.IsLoading)
        {
            _ = ViewModel.RefreshAsync();
        }
    }

    /// <summary>Re-reads the view model. Called by the window on every relevant change.</summary>
    public void Update()
    {
        if (ViewModel == null) return;

        LoadingRing.IsActive = ViewModel.IsLoading;
        LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        Rebuild();

        var hasRows = Rows.Count > 0;
        IssueList.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        StatePanel.Visibility = hasRows || ViewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;

        if (StatePanel.Visibility != Visibility.Visible) return;

        var connected = ViewModel.IsConfigured;
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (!connected)
        {
            StateGlyph.Text = "\uE71B";   // Link
            StateText.Text = "No Jira account connected yet.";
        }
        else if (ViewModel.HasError)
        {
            // A rejected JQL lands here, and Jira says precisely what it disliked.
            StateGlyph.Text = "\uE783";   // Error
            StateText.Text = ViewModel.ErrorMessage ?? string.Empty;
        }
        else
        {
            StateGlyph.Text = "\uE721";   // Search
            StateText.Text = ViewModel.HasSearched
                ? "No issues match this query."
                : "Run the query to see your issues.";
        }
    }

    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            Rows.Clear();

            foreach (var group in ViewModel.Groups)
            {
                Rows.Add(new KanbanRow
                {
                    Title = group.Status,
                    Trailing = group.Count.ToString(),
                    IsHeader = true,
                    Category = group.Category
                });

                foreach (var issue in group.Issues)
                {
                    Rows.Add(new KanbanRow
                    {
                        Title = issue.Summary,
                        Subtitle = issue.Key,
                        Trailing = issue.Priority ?? string.Empty,
                        Category = issue.StatusCategory,
                        Issue = issue
                    });
                }
            }

            // The selected issue survives a refresh; the row holding it does not.
            IssueList.SelectedItem = ViewModel.SelectedIssue == null
                ? null
                : Rows.FirstOrDefault(r => r.Issue?.Key == ViewModel.SelectedIssue.Key);
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>Headers are not selectable — disabling the container is what enforces that.</summary>
    private void IssueList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is KanbanRow row) args.ItemContainer.IsEnabled = !row.IsHeader;
    }

    private void IssueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        ViewModel.SelectedIssue = (IssueList.SelectedItem as KanbanRow)?.Issue;
    }

    private void JqlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        e.Handled = true;
        Run();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Run();

    private void Run()
    {
        if (ViewModel.IsLoading) return;

        ViewModel.Jql = JqlTextBox.Text;
        _ = ViewModel.RefreshAsync();
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when the empty state's button asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;
}
