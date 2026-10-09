using System;
using System.Linq;
using GitHalls.App.ViewModels;
using GitHalls.Core.Graph;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

public sealed partial class HistorySidebarPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; } = null!;

    public HistorySidebarPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RepositoryViewModel vm)
        {
            if (ViewModel?.GitgraphViewModel != null)
            {
                ViewModel.GitgraphViewModel.PropertyChanged -= GitgraphViewModel_PropertyChanged;
            }

            ViewModel = vm;
            DataContext = ViewModel;

            if (ViewModel?.GitgraphViewModel != null)
            {
                ViewModel.GitgraphViewModel.PropertyChanged += GitgraphViewModel_PropertyChanged;
            }

            UpdateFilterSelection();

            // x:Bind resolved ViewModel once, during InitializeComponent, when it
            // was still null — and a Page raises no change notification for its
            // own properties. Without this the bound lists stay empty.
            Bindings.Update();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (ViewModel?.GitgraphViewModel != null)
        {
            ViewModel.GitgraphViewModel.PropertyChanged -= GitgraphViewModel_PropertyChanged;
        }
        base.OnNavigatedFrom(e);
    }

    private void GitgraphViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GitgraphViewModel.ShowAllBranches))
        {
            UpdateFilterSelection();
        }
    }

    private void UpdateFilterSelection()
    {
        if (ViewModel?.GitgraphViewModel != null && BranchFilterBar != null)
        {
            var target = ViewModel.GitgraphViewModel.ShowAllBranches ? AllBranchesItem : CurrentBranchItem;
            if (BranchFilterBar.SelectedItem != target)
            {
                BranchFilterBar.SelectedItem = target;
            }
        }
    }

    private void BranchFilterBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ViewModel?.GitgraphViewModel == null) return;
        bool showAll = sender.SelectedItem == AllBranchesItem;
        if (ViewModel.GitgraphViewModel.ShowAllBranches != showAll)
        {
            ViewModel.GitgraphViewModel.ShowAllBranches = showAll;
        }
    }

    public Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ToErrorVisibility(string? errorMessage) => string.IsNullOrEmpty(errorMessage) ? Visibility.Collapsed : Visibility.Visible;

    private void Checkout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GraphRow row)
        {
            _ = ViewModel.GitgraphViewModel.CheckoutCommitAsync(row);
        }
    }

    private void Merge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GraphRow row)
        {
            _ = ViewModel.GitgraphViewModel.MergeCommitAsync(row);
        }
    }

    private async void CreateBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GraphRow row)
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
                await ViewModel.GitgraphViewModel.CreateBranchAtAsync(row, textBox.Text.Trim());
            }
        }
    }

    private async void DeleteBranch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is GraphRow row)
        {
            var localBranches = row.Commit.Refs.Where(r => !r.StartsWith("origin/") && !r.Contains("HEAD") && !r.StartsWith("tag:")).ToList();
            if (localBranches.Count == 0) return;

            string branchToDelete = localBranches[0];

            if (localBranches.Count > 1)
            {
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

            await ViewModel.GitgraphViewModel.DeleteBranchAsync(branchToDelete);
        }
    }
}
