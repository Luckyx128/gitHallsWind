using GitHalls.App.Controls;
using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

/// <summary>
/// Everything a commit touched, one collapsible diff per file.
/// Port of CommitDetailView.swift + CommitFileDiffSection.swift.
/// </summary>
public sealed partial class CommitDetailPage : Page
{
    /// <summary>Matches the Swift section cap: past this the section scrolls internally.</summary>
    private const double SectionMaxHeight = 2000;

    /// <summary>Sections beyond this start collapsed — a 40-file commit shouldn't render 40 diffs at once.</summary>
    private const int AutoExpandLimit = 10;

    public RepositoryViewModel ViewModel { get; private set; } = null!;

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
            Update();
        }
    }

    /// <summary>Re-reads the view model and rebuilds the pane.</summary>
    public void Update()
    {
        if (ViewModel == null) return;

        var detail = ViewModel.SelectedCommitDetail;
        var loading = ViewModel.IsLoadingCommitDetail;

        // Show the detail as soon as there is one, even while a newer load runs:
        // re-selecting the commit already shown shouldn't blank the pane.
        if (detail != null)
        {
            ShowDetail(detail);
            return;
        }

        FileSections.Children.Clear();
        DetailScroller.Visibility = Visibility.Collapsed;

        LoadingRing.IsActive = loading;
        LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        PlaceholderPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderText.Text = ViewModel.SelectedCommit == null ? "Select a commit" : "No details to show";
    }

    private void ShowDetail(CommitDetail detail)
    {
        PlaceholderPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        DetailScroller.Visibility = Visibility.Visible;

        var commit = detail.Commit;
        SummaryText.Text = commit.Summary;
        AuthorText.Text = commit.AuthorName;
        DateText.Text = commit.Date.ToLocalTime().ToString("d MMM yyyy, HH:mm");
        HashText.Text = commit.ShortHash;

        var body = commit.Message.Length > commit.Summary.Length
            ? commit.Message.Substring(commit.Summary.Length).Trim()
            : string.Empty;
        BodyText.Text = body;
        BodyText.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        FileSections.Children.Clear();
        for (int i = 0; i < detail.FileDiffs.Count; i++)
        {
            FileSections.Children.Add(BuildSection(detail.FileDiffs[i], expanded: i < AutoExpandLimit));
        }
    }

    private static Expander BuildSection(FileDiff fileDiff, bool expanded)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(new TextBlock
        {
            Text = fileDiff.FilePath,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!fileDiff.IsBinary)
        {
            header.Children.Add(new TextBlock
            {
                Text = $"+{fileDiff.Additions}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80))
            });
            header.Children.Add(new TextBlock
            {
                Text = $"-{fileDiff.Deletions}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54))
            });
        }

        var expander = new Expander
        {
            Header = header,
            IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        if (expanded)
        {
            expander.Content = BuildDiffView(fileDiff);
        }
        else
        {
            // Built on first expand: rendering every collapsed diff up front is
            // the cost this whole page is trying to avoid.
            expander.Expanding += (sender, _) =>
            {
                if (sender.Content == null) sender.Content = BuildDiffView(fileDiff);
            };
        }

        return expander;
    }

    private static DiffTextView BuildDiffView(FileDiff fileDiff)
    {
        var view = new DiffTextView { MaxIntrinsicHeight = SectionMaxHeight };
        view.SetDiff(fileDiff);
        return view;
    }
}
