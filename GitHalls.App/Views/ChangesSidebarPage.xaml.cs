using GitHalls.App.ViewModels;
using GitHalls.Core.Commits;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

public sealed partial class ChangesSidebarPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; } = null!;

    /// <summary>The row the context menu was opened on.</summary>
    private FileChange? _contextChange;

    /// <summary>Suppresses the prefix rewrite while the type box is being populated or synced.</summary>
    private bool _updatingCommitType;

    public ChangesSidebarPage()
    {
        InitializeComponent();

        CommitTypeComboBox.ItemsSource = ConventionalCommitType.All;
        BuildTypeReference();
    }

    /// <summary>Fills the help flyout from the same list the picker uses.</summary>
    private void BuildTypeReference()
    {
        CommitTypeReference.Children.Add(new TextBlock
        {
            Text = "Conventional Commits",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 16
        });

        foreach (var type in ConventionalCommitType.All)
        {
            var entry = new StackPanel { Spacing = 1 };
            entry.Children.Add(new TextBlock
            {
                Text = type.Name,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            entry.Children.Add(new TextBlock
            {
                Text = type.Description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            CommitTypeReference.Children.Add(entry);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is RepositoryViewModel vm)
        {
            ViewModel = vm;
            DataContext = ViewModel;

            // x:Bind resolved ViewModel once, during InitializeComponent, when it
            // was still null — and a Page raises no change notification for its
            // own properties. Without this the bound lists stay empty.
            Bindings.Update();

            _updatingCommitType = true;
            CommitTypeComboBox.SelectedItem = ViewModel.CommitType;
            _updatingCommitType = false;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RepositoryViewModel.CommitType)) return;
        if (Equals(CommitTypeComboBox.SelectedItem, ViewModel.CommitType)) return;

        // A successful commit resets the type; the picker must not keep showing it.
        _updatingCommitType = true;
        CommitTypeComboBox.SelectedItem = ViewModel.CommitType;
        _updatingCommitType = false;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (ViewModel != null) ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void CommitTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCommitType) return;

        ViewModel.CommitType = CommitTypeComboBox.SelectedItem as ConventionalCommitType;
        ViewModel.ApplyConventionalPrefix();
    }

    // The scope is applied on commit, not on every keystroke — rewriting the
    // summary mid-word would fight whoever is typing in it.
    private void CommitScope_Committed(object sender, RoutedEventArgs e) => ViewModel.ApplyConventionalPrefix();

    private void CommitScope_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.ApplyConventionalPrefix();
        CommitSummaryTextBox.Focus(FocusState.Programmatic);
    }

    private async void SuggestButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SuggestCommitTypeAsync();

        // Reflect the guess in the picker without it re-triggering the rewrite,
        // then apply the prefix once.
        _updatingCommitType = true;
        CommitTypeComboBox.SelectedItem = ViewModel.CommitType;
        _updatingCommitType = false;

        ViewModel.ApplyConventionalPrefix();
        CommitSummaryTextBox.Focus(FocusState.Programmatic);
    }

    private async void StageAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        // The click already flipped the visual state; the view model still holds
        // the state we are toggling away from, so decide from that. Anything but
        // "everything is staged" means the useful action is to stage.
        var stage = ViewModel.StagedState != true;

        await ViewModel.SetAllStagedAsync(stage);

        // The OneWay binding only repaints when StagedState actually changed, so
        // a failed git call would otherwise leave the box lying about the state.
        StageAllCheckBox.IsChecked = ViewModel.StagedState;
    }

    private void ChangeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: FileChange change } checkBox) return;

        if (checkBox.IsChecked == true)
        {
            ViewModel.StageCommand.Execute(change);
        }
        else
        {
            ViewModel.UnstageCommand.Execute(change);
        }
    }

    private void ChangesContextFlyout_Opening(object? sender, object e)
    {
        _contextChange = ChangesList.SelectedItem as FileChange;
        DiscardMenuItem.IsEnabled = _contextChange != null;
        DiscardMenuItem.Text = _contextChange == null
            ? "Discard Changes..."
            : $"Discard Changes in {_contextChange.FileName}...";
    }

    private async void DiscardMenuItem_Click(object sender, RoutedEventArgs e)
        => await ConfirmAndDiscardAsync(_contextChange);

    private async void DiscardButton_Click(object sender, RoutedEventArgs e)
        => await ConfirmAndDiscardAsync(ChangesList.SelectedItem as FileChange);

    private async Task ConfirmAndDiscardAsync(FileChange? change)
    {
        if (change == null) return;

        // Discarding cannot be undone through git, so it always asks first.
        var dialog = new ContentDialog
        {
            Title = "Discard changes?",
            Content = $"All changes to \"{change.Path}\" will be lost. This can't be undone.",
            PrimaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DiscardAsync(change);
        }
    }
}
