using GitHalls.App.ViewModels;
using GitHalls.Core.Diff;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

/// <summary>
/// What a commit touched: the file list first, and the diff of the one file the
/// user opens. Port of CommitDetailView.swift + CommitFileDiffSection.swift.
///
/// It used to render a diff per file, ten of them expanded on arrival, over a
/// list the view model had filled with one git call per file. Selecting a commit
/// in a repository of any size stalled the window; now it costs one call, and a
/// second one when a file is picked.
/// </summary>
public sealed partial class CommitDetailPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; } = null!;

    /// <summary>
    /// What the diff view is currently showing. One click raises several property
    /// changes, and rendering a diff is the expensive part of this page — without
    /// this, each of them re-rendered the same text.
    /// </summary>
    private FileDiff? _shownDiff;

    public CommitDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RepositoryViewModel vm)
        {
            ViewModel = vm;
            DataContext = ViewModel;

            // x:Bind resolved ViewModel once, during InitializeComponent, when it
            // was still null — and a Page raises no change notification for its
            // own properties. Without this the file list stays empty.
            Bindings.Update();

            Update();
        }
    }

    /// <summary>Re-reads the view model and rebuilds the pane.</summary>
    public void Update()
    {
        if (ViewModel == null) return;

        var commit = ViewModel.SelectedCommit;
        var hasRepo = !string.IsNullOrEmpty(ViewModel.RepositoryPath);
        if (commit == null)
        {
            DetailRoot.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Visible;
            PlaceholderText.Text = hasRepo ? "Select a commit" : "Open a repository to get started.";
            return;
        }

        // The header is known the moment a commit is selected, so it appears
        // before the file list finishes loading.
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        DetailRoot.Visibility = Visibility.Visible;

        SummaryText.Text = commit.Summary;
        AuthorText.Text = commit.AuthorName;
        DateText.Text = commit.Date.ToLocalTime().ToString("d MMM yyyy, HH:mm");
        HashText.Text = commit.ShortHash;

        var body = commit.Message.Length > commit.Summary.Length
            ? commit.Message.Substring(commit.Summary.Length).Trim()
            : string.Empty;
        BodyText.Text = body;
        BodyText.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateFilesHeader();
        UpdateDiff();
    }

    private void UpdateFilesHeader()
    {
        if (ViewModel.IsLoadingCommitFiles)
        {
            FilesHeaderText.Text = "Loading files...";
            return;
        }

        var count = ViewModel.CommitFiles.Count;
        FilesHeaderText.Text = count switch
        {
            0 => "No files changed",
            1 => "1 file changed",
            _ => $"{count} files changed"
        };
    }

    /// <summary>Mirrors <see cref="_shownDiff"/>: cheap to compare, and a rebuild is not.</summary>
    private BinaryFileContents? _shownPreview;

    private void UpdateDiff()
    {
        // Cheap when unchanged; the viewer only rebuilds on a real switch.
        DiffView.SideBySide = ViewModel.IsSideBySideDiff;

        var loading = ViewModel.IsLoadingCommitFileDiff;
        DiffLoadingRing.IsActive = loading;
        DiffLoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        var diff = ViewModel.CommitFileDiff;

        // Keep the previous diff on screen while the next one loads, rather than
        // blanking the pane between two clicks.
        if (loading && diff == null)
        {
            DiffPlaceholderText.Visibility = Visibility.Collapsed;
            return;
        }

        if (!ReferenceEquals(_shownDiff, diff))
        {
            _shownDiff = diff;
            DiffView.SetDiff(diff);
        }

        // Its own comparison: the preview arrives one property change after the
        // diff it belongs to, so it cannot ride on the check above.
        var preview = ViewModel.CommitFilePreview;
        if (!ReferenceEquals(_shownPreview, preview))
        {
            _shownPreview = preview;
            DiffView.SetPreview(preview);
        }

        DiffView.Visibility = diff == null ? Visibility.Collapsed : Visibility.Visible;

        DiffPlaceholderText.Visibility = diff == null && !loading ? Visibility.Visible : Visibility.Collapsed;

        // "No file changes" only once the list is in: an empty collection while
        // it loads means "not yet", not "nothing".
        DiffPlaceholderText.Text = ViewModel.CommitFiles.Count == 0 && !ViewModel.IsLoadingCommitFiles
            ? "This commit has no file changes."
            : "Select a file to see its changes.";
    }
}
