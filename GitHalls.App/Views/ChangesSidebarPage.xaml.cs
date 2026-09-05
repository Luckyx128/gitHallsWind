using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitHalls.App.Views;

public sealed partial class ChangesSidebarPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; } = null!;

    /// <summary>The row the context menu was opened on.</summary>
    private FileChange? _contextChange;

    public ChangesSidebarPage()
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
            // own properties. Without this the bound lists stay empty.
            Bindings.Update();
        }
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
