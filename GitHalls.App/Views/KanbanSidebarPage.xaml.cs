using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using Windows.UI.Text;

namespace GitHalls.App.Views;

/// <summary>
/// One row of the query list: a section header or a query.
///
/// Headers travel in the same collection as the queries, the same way the
/// branch picker builds its sections — one virtualized list instead of a list
/// per group, and no selection state spread across several controls.
/// </summary>
public sealed class QueryRow
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool IsHeader { get; set; }

    /// <summary>The query this row stands for. Null for a header.</summary>
    public JiraQuery? Query { get; set; }

    public string ToolTip => Query == null ? Title : Query.Jql;

    /// <summary>A sprint preset, a person preset, or the user's own filter.</summary>
    public string Glyph => Query switch
    {
        null => string.Empty,
        { IsBuiltIn: false } => "\uE71C",   // Filter
        { Id: JiraQueryPresets.ActiveSprintId or JiraQueryPresets.MySprintWorkId } => "\uE7C1",   // Flag
        { Id: JiraQueryPresets.RecentlyUpdatedId } => "\uE823",   // Recent
        _ => "\uE77B"                    // Contact
    };

    public double FontSize => IsHeader ? 12 : 14;
    public FontWeight FontWeight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility GlyphVisibility => IsHeader ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SubtitleVisibility => Subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// A Style, not a Brush. A Brush read out of the resources here is one
    /// theme's instance and the row goes on painting it after the user
    /// switches; the {ThemeResource} in the Style's setter is re-resolved.
    /// </summary>
    public Style TitleStyle => (Style)Application.Current.Resources[
        IsHeader ? "GHRowHeaderTextStyle" : "GHRowTitleTextStyle"];
}

/// <summary>
/// The Kanban sidebar: which query the board runs. Port of the JQL box in
/// KanbanSidebarView.swift, grown into a list — most people never write JQL,
/// and the ones who do want to keep what they wrote.
/// </summary>
public sealed partial class KanbanSidebarPage : Page
{
    public JiraViewModel ViewModel { get; private set; } = null!;

    private ObservableCollection<QueryRow> Rows { get; } = new();

    /// <summary>Set while the list is rebuilt, so restoring the selection doesn't read as a click.</summary>
    private bool _rebuilding;

    public KanbanSidebarPage()
    {
        InitializeComponent();
        QueryList.ItemsSource = Rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not JiraViewModel viewModel) return;

        ViewModel = viewModel;
        DataContext = viewModel;
        Update();
    }

    /// <summary>Re-reads the view model. Called by the window on every relevant change.</summary>
    public void Update()
    {
        if (ViewModel == null) return;

        var connected = ViewModel.IsConfigured;
        QueryList.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        AddQueryButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        StatePanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;

        if (connected) Rebuild();
    }

    private void Rebuild()
    {
        _rebuilding = true;
        try
        {
            Rows.Clear();

            var presets = ViewModel.Queries.Where(q => q.IsBuiltIn).ToList();
            var custom = ViewModel.Queries.Where(q => !q.IsBuiltIn).ToList();

            Rows.Add(new QueryRow { Title = "Queries", IsHeader = true });
            foreach (var query in presets) Rows.Add(new QueryRow { Title = query.Name, Query = query });

            if (custom.Count > 0)
            {
                Rows.Add(new QueryRow { Title = "My queries", IsHeader = true });
                foreach (var query in custom)
                {
                    Rows.Add(new QueryRow { Title = query.Name, Subtitle = query.Jql, Query = query });
                }
            }

            QueryList.SelectedItem = Rows.FirstOrDefault(r => ReferenceEquals(r.Query, ViewModel.SelectedQuery));
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>Headers are not selectable — disabling the container is what enforces that.</summary>
    private void QueryList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is QueryRow row) args.ItemContainer.IsEnabled = !row.IsHeader;
    }

    private void QueryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        if ((QueryList.SelectedItem as QueryRow)?.Query is not { } query) return;

        ViewModel.SelectedQuery = query;
    }

    // MARK: - Custom queries

    private void QueryList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not QueryRow { Query: { IsBuiltIn: false } query }) return;

        var menu = new MenuFlyout();

        var edit = new MenuFlyoutItem { Text = "Edit..." };
        edit.Click += async (_, _) => await EditQueryAsync(query);
        menu.Items.Add(edit);

        var delete = new MenuFlyoutItem { Text = "Delete" };
        delete.Click += async (_, _) => await ViewModel.RemoveQueryAsync(query);
        menu.Items.Add(delete);

        menu.ShowAt(sender as UIElement, e.GetPosition(sender as UIElement));
        e.Handled = true;
    }

    private async void AddQuery_Click(object sender, RoutedEventArgs e)
    {
        var result = await QueryDialog.ShowAsync(XamlRoot, "New query", string.Empty, string.Empty);
        if (result == null) return;

        await ViewModel.AddQueryAsync(result.Value.Name, result.Value.Jql);
    }

    private async Task EditQueryAsync(JiraQuery query)
    {
        var result = await QueryDialog.ShowAsync(XamlRoot, "Edit query", query.Name, query.Jql);
        if (result == null) return;

        await ViewModel.UpdateQueryAsync(query, result.Value.Name, result.Value.Jql);
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised when the empty state's button asks for the settings window.</summary>
    public event EventHandler? SettingsRequested;
}

/// <summary>
/// Name + JQL, in a ContentDialog. Built in code because it is two boxes and a
/// hint, and a page of XAML would say less than this does.
/// </summary>
internal static class QueryDialog
{
    public static async Task<(string Name, string Jql)?> ShowAsync(XamlRoot xamlRoot, string title, string name, string jql)
    {
        var nameBox = new TextBox { Header = "Name", Text = name, PlaceholderText = "Bugs in my project" };
        var jqlBox = new TextBox
        {
            Header = "JQL",
            Text = jql,
            PlaceholderText = "project = APP AND type = Bug ORDER BY priority DESC",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            FontFamily = new FontFamily("Cascadia Mono, Consolas")
        };
        var hint = new TextBlock
        {
            Text = "Jira Query Language, exactly as in Jira's own search. currentUser() and openSprints() work here too.",
            Style = (Style)Application.Current.Resources["GHBodyMutedTextStyle"]
        };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = Valid(nameBox.Text, jqlBox.Text),
            Content = new StackPanel { Spacing = 10, MinWidth = 420, Children = { nameBox, jqlBox, hint } }
        };

        void Validate(object _, TextChangedEventArgs __) => dialog.IsPrimaryButtonEnabled = Valid(nameBox.Text, jqlBox.Text);
        nameBox.TextChanged += Validate;
        jqlBox.TextChanged += Validate;

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? (nameBox.Text.Trim(), jqlBox.Text.Trim()) : null;
    }

    private static bool Valid(string name, string jql) =>
        !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(jql);
}
