using GitHalls.App.ViewModels;
using GitHalls.Core.Diff;
using GitHalls.Core.Markdown;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace GitHalls.App.Views;

public sealed partial class DiffPage : Page
{
    public DiffPage()
    {
        InitializeComponent();

        DiffView.SelectionChanged += (_, count) => UpdateSelectionBar(count);
        DiffView.HunkActionInvoked += async (_, hunk) =>
        {
            if (_viewModel != null) await _viewModel.ApplyHunkAsync(hunk);
        };
    }

    /// <summary>
    /// The repository's README, when there is one, in place of the "select a
    /// file" line. Rebuilt only when the document actually changed: this runs
    /// on every property change the window forwards.
    /// </summary>
    private IReadOnlyList<MarkdownBlock>? _shownReadme;

    private void ShowReadme()
    {
        var readme = _viewModel?.Readme;
        var hasReadme = readme is { Count: > 0 };
        var hasRepo = !string.IsNullOrEmpty(_viewModel?.RepositoryPath);

        if (!ReferenceEquals(_shownReadme, readme))
        {
            _shownReadme = readme;
            ReadmeView.SetBlocks(hasReadme ? readme : null);
            ReadmeNameText.Text = _viewModel?.ReadmeFileName ?? string.Empty;
        }

        ReadmeCard.Visibility = hasReadme ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasReadme ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = hasRepo ? "Select a file to see its changes." : "Open a repository to get started.";
    }

    /// <summary>Set by the window alongside the diff, since the page has no view model of its own.</summary>
    private RepositoryViewModel? _viewModel;

    public void SetViewModel(RepositoryViewModel viewModel) => _viewModel = viewModel;

    /// <summary>Unified or side by side. Kept out of the page's own state: the
    /// window owns the preference and hands it over with every update.</summary>
    public void SetSideBySide(bool sideBySide) => DiffView.SideBySide = sideBySide;

    public void UpdateDiff(FileDiff? diff) => UpdateDiff(diff, null);

    /// <summary>
    /// What the viewer is currently showing. Update runs several times per click
    /// — once per property the window forwards — and re-rendering a diff means
    /// laying out every one of its lines, so an unchanged one is left alone.
    /// Keeping it also means the picked rows survive an update that only moved
    /// the counts on the toggle.
    /// </summary>
    private FileDiff? _shownDiff;
    private BinaryFileContents? _shownPreview;

    public void UpdateDiff(FileDiff? diff, BinaryFileContents? preview)
    {
        if (!ReferenceEquals(_shownDiff, diff))
        {
            _shownDiff = diff;

            // Before the diff, so the gutter is measured with its selection
            // column already accounted for.
            DiffView.SelectionEnabled = diff is { CanBuildPatch: true };
            DiffView.HunkActionLabel = diff?.Side == DiffSide.Index ? "Unstage hunk" : "Stage hunk";
            DiffView.SetDiff(diff);
        }

        if (!ReferenceEquals(_shownPreview, preview))
        {
            _shownPreview = preview;
            DiffView.SetPreview(preview);
        }

        UpdateSidePanel();
        UpdateSelectionBar(DiffView.SelectedCount);

        if (diff == null)
        {
            FilePathText.Text = string.Empty;
            StatsPanel.Visibility = Visibility.Collapsed;
            // The whole card goes, not just its contents: an empty card framing
            // the placeholder text would read as a failed load.
            DiffCard.Visibility = Visibility.Collapsed;
            ShowReadme();
            return;
        }

        FilePathText.Text = diff.FilePath;
        DiffCard.Visibility = Visibility.Visible;
        ReadmeCard.Visibility = Visibility.Collapsed;
        ReadmeView.SetBlocks(null);
        EmptyText.Visibility = Visibility.Collapsed;

        if (diff.IsBinary)
        {
            StatsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            AdditionsText.Text = $"+{diff.Additions}";
            DeletionsText.Text = $"-{diff.Deletions}";
            StatsPanel.Visibility = Visibility.Visible;
        }
    }

    // MARK: - Unstaged / staged

    /// <summary>
    /// The toggle only appears where there is a choice to make: a file with work
    /// on one side alone reads better without it.
    /// </summary>
    private void UpdateSidePanel()
    {
        var working = _viewModel?.WorkingTreeDiff;
        var index = _viewModel?.IndexDiff;

        var workingCount = ChangedLineCount(working);
        var indexCount = ChangedLineCount(index);

        if (workingCount == 0 && indexCount == 0)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            return;
        }

        SidePanel.Visibility = Visibility.Visible;
        UnstagedToggle.Content = $"Unstaged ({workingCount})";
        StagedToggle.Content = $"Staged ({indexCount})";

        var shown = _shownDiff;
        WholeFileHint.Visibility = shown is { IsBinary: false, CanBuildPatch: false } && ChangedLineCount(shown) > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var staged = _viewModel?.DiffSideSelection == DiffSide.Index;
        UnstagedToggle.IsChecked = !staged;
        StagedToggle.IsChecked = staged;
    }

    private static int ChangedLineCount(FileDiff? diff) => diff == null ? 0 : diff.Additions + diff.Deletions;

    private void SideToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || sender is not ToggleButton button) return;

        var side = (string)button.Tag == "Index" ? DiffSide.Index : DiffSide.WorkingTree;

        // Clicking the side already shown would otherwise untick it and leave
        // neither on.
        if (_viewModel.DiffSideSelection == side)
        {
            UpdateSidePanel();
            return;
        }

        _viewModel.DiffSideSelection = side;
    }

    // MARK: - Selection bar

    private void UpdateSelectionBar(int count)
    {
        if (count == 0 || _viewModel == null)
        {
            SelectionBar.Visibility = Visibility.Collapsed;
            return;
        }

        var unstaging = _viewModel.DiffSideSelection == DiffSide.Index;

        SelectionCountText.Text = count == 1 ? "1 line selected" : $"{count} lines selected";
        ApplySelectionButton.Content = unstaging ? "Unstage" : "Stage";
        SelectionBar.Visibility = Visibility.Visible;
    }

    private async void ApplySelection_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var selection = DiffView.SelectedLineIndices;
        if (selection.Count == 0) return;

        await _viewModel.ApplySelectionAsync(selection);
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => DiffView.ClearSelection();
}
