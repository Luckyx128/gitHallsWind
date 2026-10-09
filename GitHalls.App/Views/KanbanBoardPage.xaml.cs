using System;
using System.Collections.Generic;
using System.Linq;
using GitHalls.App.Themes;
using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;

namespace GitHalls.App.Views;

public sealed partial class KanbanBoardPage : Page
{
    public JiraViewModel ViewModel { get; private set; } = null!;

    private IReadOnlyList<JiraIssueGroup>? _shownColumns;
    private MenuFlyout? _openMenu;
    private readonly Dictionary<string, ListViewItem> _containersByKey = new(StringComparer.Ordinal);
    private JiraIssue? _draggedIssue;

    public event EventHandler<JiraIssue>? IssueOpened;
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

        if (ViewModel.IsConfigured && !ViewModel.HasSearched && !ViewModel.IsLoading)
        {
            _ = ViewModel.RefreshAsync();
        }
    }

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

        ActionInfoBar.Message = ViewModel.ActionMessage ?? string.Empty;
        ActionInfoBar.Severity = ViewModel.ActionFailed ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        ActionInfoBar.IsOpen = !string.IsNullOrEmpty(ViewModel.ActionMessage);

        var hasColumns = columns.Count > 0;
        BoardScroller.Visibility = hasColumns ? Visibility.Visible : Visibility.Collapsed;
        FilterTextBox.IsEnabled = ViewModel.IssueCount > 0;
        StatePanel.Visibility = hasColumns || (ViewModel.IsLoading && !ViewModel.HasError)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (StatePanel.Visibility == Visibility.Visible)
        {
            var connected = ViewModel.IsConfigured;
            ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            if (!connected)
            {
                StateGlyph.Glyph = "\uE71B";
                StateTile.Background = GHBrush.Get("GHKanbanTintBrush");
                StateGlyph.Foreground = GHBrush.Get("GHKanbanBrush");
                StateText.Text = "Connect a Jira account to see your board here.";
            }
            else if (ViewModel.HasError)
            {
                StateGlyph.Glyph = "\uE783";
                StateTile.Background = GHBrush.Get("GHDeletionTintBrush");
                StateGlyph.Foreground = GHBrush.Get("GHDeletionBrush");
                StateText.Text = ViewModel.ErrorMessage ?? string.Empty;
            }
            else
            {
                StateGlyph.Glyph = "\uE7C1";
                StateTile.Background = GHBrush.Get("GHKanbanTintBrush");
                StateGlyph.Foreground = GHBrush.Get("GHKanbanBrush");
                StateText.Text = ViewModel.HasSearched
                    ? "No issues match this query. If your project has no active sprint, try another query."
                    : "Run the query to see your issues.";
            }
        }
        else
        {
            CreateIssueButton.Visibility = ViewModel.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RebuildColumns(IReadOnlyList<JiraIssueGroup> columns)
    {
        _openMenu?.Hide();
        _openMenu = null;
        _containersByKey.Clear();
        ColumnsPanel.Children.Clear();
        foreach (var column in columns) ColumnsPanel.Children.Add(BuildColumn(column));
    }

    public void UpdateBusyCards()
    {
        if (ViewModel == null) return;

        foreach (var (key, container) in _containersByKey)
        {
            var busy = ViewModel.IsIssueBusy(key);
            container.IsEnabled = !busy;
            container.Opacity = busy ? 0.5 : 1.0;
        }
    }

    private Border BuildColumn(JiraIssueGroup column)
    {
        var header = new Grid { Padding = new Thickness(14, 10, 12, 8), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = GHBrush.JiraCategory(column.Category),
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
                Style = (Style)Resources["BoardSecondaryTextStyle"]
            }
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        var listView = new ListView
        {
            ItemsSource = column.Issues,
            ItemTemplate = (DataTemplate)Resources["JiraIssueCardTemplate"],
            SelectionMode = ListViewSelectionMode.None,
            CanDragItems = true,
            AllowDrop = true,
            IsItemClickEnabled = true,
            Style = (Style)Application.Current.Resources["GHListViewStyle"],
            ItemContainerStyle = (Style)Application.Current.Resources["GHListItemStyle"],
            Tag = column
        };

        listView.ItemClick += ListView_ItemClick;
        listView.DragItemsStarting += ListView_DragItemsStarting;
        listView.DragOver += ListView_DragOver;
        listView.Drop += ListView_Drop;
        listView.ContainerContentChanging += ListView_ContainerContentChanging;

        if (column.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Nothing here",
                FontSize = 12,
                Margin = new Thickness(14, 4, 0, 0),
                Style = (Style)Resources["BoardTertiaryTextStyle"]
            };
            var panel = new Grid();
            panel.Children.Add(emptyText);
            panel.Children.Add(listView);
            Grid.SetRow(panel, 1);
            
            var layoutEmpty = new Grid();
            layoutEmpty.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutEmpty.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layoutEmpty.Children.Add(header);
            layoutEmpty.Children.Add(panel);

            return new Border
            {
                Style = (Style)Application.Current.Resources["GHContentCardStyle"],
                Width = 280,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = layoutEmpty
            };
        }

        Grid.SetRow(listView, 1);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        layout.Children.Add(listView);

        return new Border
        {
            Style = (Style)Application.Current.Resources["GHContentCardStyle"],
            Width = 280,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = layout
        };
    }

    private void ListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is JiraIssue issue && args.ItemContainer is ListViewItem container)
        {
            _containersByKey[issue.Key] = container;
            var busy = ViewModel.IsIssueBusy(issue.Key);
            container.IsEnabled = !busy;
            container.Opacity = busy ? 0.5 : 1.0;
        }
    }

    private void ListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is JiraIssue issue) IssueOpened?.Invoke(this, issue);
    }

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is JiraIssue issue)
        {
            _draggedIssue = issue;
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedIssue != null)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void ListView_Drop(object sender, DragEventArgs e)
    {
        var issue = _draggedIssue;
        _draggedIssue = null;
        if (issue == null || sender is not ListView listView || listView.Tag is not JiraIssueGroup targetColumn) return;

        if (issue.Status == targetColumn.Status) return;

        var transitions = await ViewModel.TransitionsForAsync(issue);
        var transition = transitions.FirstOrDefault(t => t.ToStatus == targetColumn.Status);
        if (transition != null)
        {
            _ = ViewModel.MoveIssueAsync(issue, transition);
        }
    }

    private async void CardMenu_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        if (menu.Target?.DataContext is not JiraIssue issue) return;

        _openMenu = menu;
        menu.Items.Clear();
        menu.Items.Add(Disabled("Loading moves\u2026"));

        IReadOnlyList<JiraTransition> transitions;
        try
        {
            transitions = await ViewModel.TransitionsForAsync(issue);
        }
        catch (Exception ex)
        {
            if (menu.IsOpen) Replace(menu, Disabled(ex.Message));
            return;
        }

        if (!menu.IsOpen || !ReferenceEquals(_openMenu, menu)) return;

        menu.Items.Clear();

        var editItem = new MenuFlyoutItem { Text = "Edit issue" };
        editItem.Click += async (_, _) => await ShowEditDialogAsync(issue);
        menu.Items.Add(editItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        if (transitions.Count == 0)
        {
            menu.Items.Add(Disabled("No moves available"));
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

            item.Click += (_, _) => _ = ViewModel.MoveIssueAsync(issue, transition);
            menu.Items.Add(item);
        }

        menu.Items.Add(AssignItem(issue));
    }

    private async Task ShowEditDialogAsync(JiraIssue issue)
    {
        var summaryTextBox = new TextBox { Text = issue.Summary, Header = "Summary", Width = 400 };
        var typeTextBox = new TextBox { Text = issue.Type, Header = "Issue Type", Width = 400, Margin = new Thickness(0, 12, 0, 0) };

        var dialog = new ContentDialog
        {
            Title = $"Edit {issue.Key}",
            Content = new StackPanel { Children = { summaryTextBox, typeTextBox } },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var parameters = new JiraIssueUpdateParameters
            {
                Summary = string.IsNullOrWhiteSpace(summaryTextBox.Text) ? null : summaryTextBox.Text,
                IssueTypeName = string.IsNullOrWhiteSpace(typeTextBox.Text) ? null : typeTextBox.Text
            };
            _ = ViewModel.UpdateIssueAsync(issue, parameters);
        }
    }

    private MenuFlyoutItemBase AssignItem(JiraIssue issue)
    {
        var mine = ViewModel.MyAccountId;
        if (mine != null && issue.AssigneeAccountId == mine) return Disabled("Assigned to you");

        var item = new MenuFlyoutItem { Text = "Assign to me" };
        item.Click += (_, _) => _ = ViewModel.AssignToMeAsync(issue);
        return item;
    }

    private static MenuFlyoutItem Disabled(string text) => new() { Text = text, IsEnabled = false };

    private static void Replace(MenuFlyout menu, MenuFlyoutItemBase item)
    {
        menu.Items.Clear();
        menu.Items.Add(item);
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.FilterText = FilterTextBox.Text;
    }

    private void ActionInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearActionMessage();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLoading) return;
        _ = ViewModel.RefreshAsync();
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private async void CreateIssue_Click(object sender, RoutedEventArgs e)
    {
        var projectTextBox = new TextBox { Header = "Project Key", PlaceholderText = "e.g. PROJ", Width = 400 };
        var summaryTextBox = new TextBox { Header = "Summary", Width = 400, Margin = new Thickness(0, 12, 0, 0) };
        var typeTextBox = new TextBox { Header = "Issue Type", Text = "Task", Width = 400, Margin = new Thickness(0, 12, 0, 0) };

        var dialog = new ContentDialog
        {
            Title = "Create Issue",
            Content = new StackPanel { Children = { projectTextBox, summaryTextBox, typeTextBox } },
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var parameters = new JiraIssueCreateParameters(
                ProjectKey: projectTextBox.Text,
                Summary: summaryTextBox.Text,
                IssueTypeName: typeTextBox.Text
            );
            _ = ViewModel.CreateIssueAsync(parameters);
        }
    }
}
