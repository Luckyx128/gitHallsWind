using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace GitHalls.App.Controls;

/// <summary>One saved identity, as the switcher lists it.</summary>
public sealed class IdentityRow
{
    public GitIdentity Identity { get; init; } = new();
    public bool IsActive { get; init; }

    public string Title => Identity.DisplayName;
    public string Subtitle => Identity.Email;
    public string ToolTip => $"{Identity.Name} <{Identity.Email}>";
    public Visibility CheckVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Picks who commits in this repository. Built like the branch switcher — a
/// click acts, rather than selecting something a second button then confirms —
/// because switching identity is as reversible as switching branch.
/// </summary>
public sealed partial class IdentitySwitcher : UserControl
{
    private RepositoryViewModel? _viewModel;

    public ObservableCollection<IdentityRow> Rows { get; } = new();

    /// <summary>Raised once an identity has been applied, so the host can close its flyout.</summary>
    public event EventHandler? ActionCompleted;

    /// <summary>Raised when the user asks for the settings window.</summary>
    public event EventHandler? ManageRequested;

    public IdentitySwitcher()
    {
        InitializeComponent();
        IdentityList.ItemsSource = Rows;
    }

    /// <summary>Reloads from the view model. Call each time the flyout opens.</summary>
    public void Load(RepositoryViewModel viewModel)
    {
        _viewModel = viewModel;

        var author = viewModel.CurrentAuthor;
        CurrentAuthorText.Text = author?.ToString() ?? "No identity configured";
        ScopeText.Text = author == null
            ? "git will refuse to commit until one is set"
            : viewModel.HasLocalIdentityOverride
                ? "Set for this repository"
                : "Inherited from your global config";

        Rows.Clear();
        foreach (var identity in viewModel.SavedIdentities)
        {
            Rows.Add(new IdentityRow { Identity = identity, IsActive = identity.Matches(author) });
        }

        // Nothing to switch to yet: the empty list would say nothing on its own.
        IdentityList.Visibility = Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void IdentityList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_viewModel == null) return;
        if (e.ClickedItem is not IdentityRow row) return;

        ActionCompleted?.Invoke(this, EventArgs.Empty);
        await _viewModel.SetIdentityAsync(row.Identity, FixRemoteCheckBox.IsChecked == true);
    }

    private void Manage_Click(object sender, RoutedEventArgs e)
    {
        ActionCompleted?.Invoke(this, EventArgs.Empty);
        ManageRequested?.Invoke(this, EventArgs.Empty);
    }
}
