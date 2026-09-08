using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace GitHalls.App.Views;

/// <summary>
/// The board: one column per status, a card per issue. Clicking a card opens
/// the issue in its own window — the card is a summary, not a place to read.
///
/// Columns are built in code rather than templated: a board has a handful of
/// columns and at most a hundred cards, and the shape (a column is a card
/// holding a scroller holding buttons) is easier to read as a method than as
/// three nested templates.
/// </summary>
public sealed partial class KanbanBoardPage : Page
{
    public JiraViewModel ViewModel { get; private set; } = null!;

    /// <summary>What the board currently shows, to skip rebuilding it for an unrelated change.</summary>
    private IReadOnlyList<JiraIssueGroup>? _shownColumns;

    /// <summary>Raised with the issue whose card was clicked.</summary>
    public event EventHandler<JiraIssue>? IssueOpened;

    /// <summary>Raised when the empty state's button asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;

    public KanbanBoardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not JiraViewModel viewModel) return;

        ViewModel = viewModel;
        DataContext = viewModel;
        FilterTextBox.Text = viewModel.FilterText;

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

        QueryTitleText.Text = ViewModel.SelectedQuery.Name;
        ToolTipService.SetToolTip(QueryTitleText, ViewModel.SelectedQuery.Jql);

        LoadingRing.IsActive = ViewModel.IsLoading;
        LoadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;

        var columns = ViewModel.Columns;
        if (!ReferenceEquals(_shownColumns, columns))
        {
            _shownColumns = columns;
            RebuildColumns(columns);
        }

        var shownCount = columns.Sum(c => c.Count);
        CountText.Text = ViewModel.IsLoading && !ViewModel.HasSearched
            ? "Loading..."
            : shownCount == ViewModel.IssueCount
                ? $"{ViewModel.IssueCount} issues"
                : $"{shownCount} of {ViewModel.IssueCount} issues";

        var hasColumns = columns.Count > 0;
        BoardScroller.Visibility = hasColumns ? Visibility.Visible : Visibility.Collapsed;
        FilterTextBox.IsEnabled = ViewModel.IssueCount > 0;
        StatePanel.Visibility = hasColumns || (ViewModel.IsLoading && !ViewModel.HasError)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (StatePanel.Visibility != Visibility.Visible) return;

        var connected = ViewModel.IsConfigured;
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (!connected)
        {
            StateGlyph.Text = "\uE71B";   // Link
            StateText.Text = "Connect a Jira account to see your board here.";
        }
        else if (ViewModel.HasError)
        {
            // A rejected JQL lands here, and Jira says precisely what it disliked.
            StateGlyph.Text = "\uE783";   // Error
            StateText.Text = ViewModel.ErrorMessage ?? string.Empty;
        }
        else
        {
            StateGlyph.Text = "\uE7C1";   // Flag
            StateText.Text = ViewModel.HasSearched
                ? "No issues match this query. If your project has no active sprint, try another query."
                : "Run the query to see your issues.";
        }
    }

    // MARK: - Building the board

    private void RebuildColumns(IReadOnlyList<JiraIssueGroup> columns)
    {
        ColumnsPanel.Children.Clear();
        foreach (var column in columns) ColumnsPanel.Children.Add(BuildColumn(column));
    }

    private Border BuildColumn(JiraIssueGroup column)
    {
        var header = new Grid { Padding = new Thickness(14, 10, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = CategoryBrush(column.Category),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var title = new TextBlock
        {
            Text = column.Status,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, title } };
        Grid.SetColumn(titleRow, 0);
        header.Children.Add(titleRow);

        var count = new Border
        {
            Style = (Style)Application.Current.Resources["GHChipStyle"],
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = column.Count.ToString(),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        var cards = new StackPanel { Spacing = 8, Padding = new Thickness(8, 0, 8, 8) };
        foreach (var issue in column.Issues) cards.Children.Add(BuildCard(issue));

        if (column.Count == 0)
        {
            cards.Children.Add(new TextBlock
            {
                Text = "Nothing here",
                FontSize = 12,
                Margin = new Thickness(6, 4, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
        }

        var scroller = new ScrollViewer
        {
            Content = cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        Grid.SetRow(scroller, 1);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        layout.Children.Add(scroller);

        return new Border
        {
            Style = (Style)Application.Current.Resources["GHContentCardStyle"],
            Width = 280,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = layout
        };
    }

    private Button BuildCard(JiraIssue issue)
    {
        var summary = new TextBlock
        {
            Text = issue.Summary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var meta = new TextBlock
        {
            Text = MetaLine(issue),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        var content = new StackPanel { Spacing = 6, Children = { summary, meta } };

        if (!string.IsNullOrWhiteSpace(issue.AssigneeName))
        {
            content.Children.Add(new TextBlock
            {
                Text = issue.AssigneeName,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        var card = new Button
        {
            Content = content,
            Tag = issue,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(4)
        };
        ToolTipService.SetToolTip(card, $"{issue.Key} — {issue.Summary}");
        card.Click += Card_Click;
        return card;
    }

    /// <summary>KEY · Type · Priority — the key first, since it is what people say out loud.</summary>
    private static string MetaLine(JiraIssue issue)
    {
        var parts = new List<string> { issue.Key, issue.Type };
        if (!string.IsNullOrWhiteSpace(issue.Priority)) parts.Add(issue.Priority);
        return string.Join(" · ", parts);
    }

    /// <summary>Jira's three categories, in the colours the app already uses for state.</summary>
    private static Brush CategoryBrush(string category) => new SolidColorBrush(category switch
    {
        "done" => Microsoft.UI.Colors.MediumSeaGreen,
        "indeterminate" => Microsoft.UI.Colors.SteelBlue,
        _ => Microsoft.UI.Colors.Gray
    });

    // MARK: - Actions

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is JiraIssue issue) IssueOpened?.Invoke(this, issue);
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterTextBox.Text;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading) return;
        _ = ViewModel.RefreshAsync();
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
