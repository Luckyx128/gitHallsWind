using GitHalls.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

public sealed partial class GitgraphPage : Page
{
    public GitgraphViewModel ViewModel { get; private set; } = null!;

    public GitgraphPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RepositoryViewModel repoViewModel)
        {
            ViewModel = repoViewModel.GitgraphViewModel;
            DataContext = ViewModel;
            Bindings.Update();
        }
    }

    public Microsoft.UI.Xaml.Visibility ToVisibility(bool isVisible) => isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ToErrorVisibility(string? errorMessage) => string.IsNullOrEmpty(errorMessage) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    private void Checkout_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GitHalls.Core.Graph.GraphRow row)
        {
            _ = ViewModel.CheckoutCommitAsync(row);
        }
    }

    private void Merge_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GitHalls.Core.Graph.GraphRow row)
        {
            _ = ViewModel.MergeCommitAsync(row);
        }
    }

    private async void CreateBranch_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GitHalls.Core.Graph.GraphRow row)
        {
            var textBox = new TextBox { PlaceholderText = "Branch name", Width = 300 };
            var dialog = new ContentDialog
            {
                Title = "Create Branch",
                Content = textBox,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                await ViewModel.CreateBranchAtAsync(row, textBox.Text.Trim());
            }
        }
    }

    private async void DeleteBranch_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GitHalls.Core.Graph.GraphRow row)
        {
            var localBranches = row.Commit.Refs.Where(r => !r.StartsWith("origin/") && !r.Contains("HEAD") && !r.StartsWith("tag:")).ToList();
            if (localBranches.Count == 0) return;

            string branchToDelete = localBranches[0];

            if (localBranches.Count > 1)
            {
                // Simple prompt since multiple branches exist
                var comboBox = new ComboBox { ItemsSource = localBranches, SelectedIndex = 0, Width = 300 };
                var dialog = new ContentDialog
                {
                    Title = "Delete Branch",
                    Content = comboBox,
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;
                branchToDelete = comboBox.SelectedItem as string ?? localBranches[0];
            }
            
            await ViewModel.DeleteBranchAsync(branchToDelete);
        }
    }
}
