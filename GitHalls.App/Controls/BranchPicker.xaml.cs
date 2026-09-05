using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using System.Collections.ObjectModel;

namespace GitHalls.App.Controls;

/// <summary>
/// One row of a branch list: either a section header or a branch.
///
/// Headers travel in the same collection as the branches so the ListView stays
/// virtualized.
/// </summary>
public sealed class BranchListItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsHeader { get; set; }
    public bool IsCurrent { get; set; }

    /// <summary>The branch this row stands for. Null for a header.</summary>
    public Branch? Branch { get; set; }

    public double FontSize => IsHeader ? 12 : 14;
    public FontWeight FontWeight => IsHeader || IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility CheckVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Brush Foreground => (Brush)Application.Current.Resources[
        IsHeader ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush"];
}

/// <summary>How a click on a branch behaves.</summary>
public enum BranchPickerMode
{
    /// <summary>Clicking a branch acts immediately (the branch switcher).</summary>
    Invoke,
    /// <summary>Clicking a branch selects it; something else confirms (the merge dialog).</summary>
    Select
}

/// <summary>
/// Filterable branch list grouped into Recent / Local / Remote. Shared by the
/// branch switcher and the merge dialog so both stay consistent.
/// </summary>
public sealed partial class BranchPicker : UserControl
{
    private RepositoryViewModel? _viewModel;

    public ObservableCollection<BranchListItem> Items { get; } = new();

    public BranchPickerMode Mode { get; set; } = BranchPickerMode.Invoke;

    /// <summary>Whether to show the "Recent" section. Off for merge, where recency says nothing.</summary>
    public bool ShowRecent { get; set; } = true;

    /// <summary>Whether the current branch appears. Off for merge — you can't merge a branch into itself.</summary>
    public bool IncludeCurrent { get; set; } = true;

    /// <summary>Raised in <see cref="BranchPickerMode.Invoke"/> when a branch is clicked.</summary>
    public event EventHandler<Branch>? BranchInvoked;

    /// <summary>Raised in <see cref="BranchPickerMode.Select"/> when the selection changes.</summary>
    public event EventHandler<Branch?>? SelectedBranchChanged;

    public Branch? SelectedBranch => (BranchList.SelectedItem as BranchListItem)?.Branch;

    public BranchPicker()
    {
        InitializeComponent();
        BranchList.ItemsSource = Items;
    }

    /// <summary>Reloads from the view model. Call each time the host is shown.</summary>
    public void Load(RepositoryViewModel viewModel)
    {
        _viewModel = viewModel;

        BranchList.SelectionMode = Mode == BranchPickerMode.Select ? ListViewSelectionMode.Single : ListViewSelectionMode.None;
        BranchList.IsItemClickEnabled = Mode == BranchPickerMode.Invoke;

        FilterTextBox.Text = string.Empty;
        Rebuild();
    }

    public void FocusFilter() => FilterTextBox.Focus(FocusState.Programmatic);

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var previous = SelectedBranch;
        Items.Clear();
        if (_viewModel == null) return;

        var filter = FilterTextBox.Text.Trim();
        bool Matches(string name) => filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var locals = _viewModel.Branches.Where(b => !b.IsRemote && (IncludeCurrent || !b.IsCurrent)).ToList();
        var remotes = _viewModel.Branches.Where(b => b.IsRemote).ToList();

        if (ShowRecent)
        {
            // Recent entries are names; resolve them to real branches so a stale
            // entry from a deleted branch can't offer an action that fails.
            var recent = _viewModel.RecentBranchNames
                .Select(name => locals.FirstOrDefault(b => b.Name == name))
                .Where(b => b != null && !b.IsCurrent && Matches(b.Name))
                .Select(b => b!)
                .ToList();

            if (recent.Count > 0)
            {
                Items.Add(Header("Recent"));
                foreach (var branch in recent) Items.Add(Row(branch, showCheck: false));
            }
        }

        var visibleLocals = locals.Where(b => Matches(b.Name)).ToList();
        if (visibleLocals.Count > 0)
        {
            Items.Add(Header("Local"));
            foreach (var branch in visibleLocals) Items.Add(Row(branch, showCheck: true));
        }

        var visibleRemotes = remotes.Where(b => Matches(b.Name)).ToList();
        if (visibleRemotes.Count > 0)
        {
            Items.Add(Header("Remote"));
            foreach (var branch in visibleRemotes) Items.Add(Row(branch, showCheck: false));
        }

        if (Items.Count == 0)
        {
            Items.Add(Header(filter.Length > 0 ? "No branches match" : "No branches"));
        }

        // Keep the selection across a filter change when it is still listed.
        if (Mode == BranchPickerMode.Select && previous != null)
        {
            var match = Items.FirstOrDefault(i => i.Branch?.Name == previous.Name);
            if (match != null) BranchList.SelectedItem = match;
            else SelectedBranchChanged?.Invoke(this, null);
        }
    }

    private static BranchListItem Header(string text) => new() { Text = text, IsHeader = true };

    private static BranchListItem Row(Branch branch, bool showCheck) => new()
    {
        Text = branch.Name,
        IsCurrent = showCheck && branch.IsCurrent,
        Branch = branch
    };

    /// <summary>Headers are not selectable or clickable — disabling the container is what enforces that.</summary>
    private void BranchList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is BranchListItem item)
        {
            args.ItemContainer.IsEnabled = !item.IsHeader;
        }
    }

    private void BranchList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (Mode != BranchPickerMode.Invoke) return;
        if (e.ClickedItem is not BranchListItem { IsHeader: false, IsCurrent: false, Branch: { } branch }) return;

        BranchInvoked?.Invoke(this, branch);
    }

    private void BranchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mode != BranchPickerMode.Select) return;

        if (BranchList.SelectedItem is BranchListItem { IsHeader: true })
        {
            BranchList.SelectedItem = null;
            return;
        }

        SelectedBranchChanged?.Invoke(this, SelectedBranch);
    }
}
