using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using GitHalls.App.ViewModels;

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
            ViewModel = vm;
            DataContext = ViewModel;

            // x:Bind resolved ViewModel once, during InitializeComponent, when it
            // was still null — and a Page raises no change notification for its
            // own properties. Without this the bound lists stay empty.
            Bindings.Update();
        }
    }
}
