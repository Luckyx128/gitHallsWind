using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using GitHalls.App.ViewModels;
using GitHalls.App.Services;

namespace GitHalls.App.Views;

public sealed partial class ChangesSidebarPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; }

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
        }
    }



    private async void CommitButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CommitMessageTextBox.Text))
        {
            await ViewModel.CommitAsync(CommitMessageTextBox.Text);
            CommitMessageTextBox.Text = "";
        }
    }

    private void ChangeCheckBox_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.CheckBox cb && cb.Tag is GitHalls.Core.Models.FileChange change)
        {
            if (cb.IsChecked == true)
            {
                ViewModel.StageCommand.Execute(change);
            }
            else
            {
                ViewModel.UnstageCommand.Execute(change);
            }
        }
    }
}
