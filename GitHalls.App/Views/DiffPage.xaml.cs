using GitHalls.Core.Diff;
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
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        FilePathText.Text = diff.FilePath;
        DiffCard.Visibility = Visibility.Visible;
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
