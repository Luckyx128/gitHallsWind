using GitHalls.App.ViewModels;
using GitHalls.Core.Diff;
using GitHalls.Core.Markdown;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App.Views;

public sealed partial class DiffPage : Page
{
    public DiffPage()
    {
        InitializeComponent();
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

        if (!ReferenceEquals(_shownReadme, readme))
        {
            _shownReadme = readme;
            ReadmeView.SetBlocks(hasReadme ? readme : null);
            ReadmeNameText.Text = _viewModel?.ReadmeFileName ?? string.Empty;
        }

        ReadmeCard.Visibility = hasReadme ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasReadme ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Set by the window alongside the diff, since the page has no view model of its own.</summary>
    private RepositoryViewModel? _viewModel;

    public void SetViewModel(RepositoryViewModel viewModel) => _viewModel = viewModel;

    /// <summary>Unified or side by side. Kept out of the page's own state: the
    /// window owns the preference and hands it over with every update.</summary>
    public void SetSideBySide(bool sideBySide) => DiffView.SideBySide = sideBySide;

    public void UpdateDiff(FileDiff? diff) => UpdateDiff(diff, null);

    public void UpdateDiff(FileDiff? diff, BinaryFileContents? preview)
    {
        DiffView.SetDiff(diff);
        DiffView.SetPreview(preview);

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
}
