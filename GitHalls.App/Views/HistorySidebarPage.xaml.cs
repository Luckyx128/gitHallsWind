using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using GitHalls.App.ViewModels;

namespace GitHalls.App.Views;

public sealed partial class HistorySidebarPage : Page
{
    public RepositoryViewModel ViewModel { get; private set; }

    public HistorySidebarPage()
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
}
