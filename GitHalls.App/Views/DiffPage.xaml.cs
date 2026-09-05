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

    public void UpdateDiff(FileDiff? diff)
    {
        DiffView.SetDiff(diff);

        if (diff == null)
        {
            FilePathText.Text = string.Empty;
            StatsPanel.Visibility = Visibility.Collapsed;
            DiffView.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        FilePathText.Text = diff.FilePath;
        DiffView.Visibility = Visibility.Visible;
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
