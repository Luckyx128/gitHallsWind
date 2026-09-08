using GitHalls.App.ViewModels;
using GitHalls.Core.GitHub;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App.Controls;

/// <summary>
/// Pull request dialog content: title, description and the branch to merge
/// into. Port of CreatePullRequestSheetView.swift.
/// </summary>
public sealed partial class CreatePullRequestSheet : UserControl
{
    /// <summary>Base entry standing for "whatever GitHub considers this repository's default".</summary>
    private const string RepositoryDefault = "Repository default";

    private RepositoryViewModel? _viewModel;

    /// <summary>What was filled in for the user, so a box they typed in is never refilled.</summary>
    private PullRequestDraft _suggestion = PullRequestDraft.Empty;

    /// <summary>Raised when a title has been typed, or emptied again, so the dialog can enable its button.</summary>
    public event EventHandler<bool>? IsValidChanged;

    public string Title => TitleBox.Text.Trim();

    public string Description => DescriptionBox.Text.Trim();

    /// <summary>Null when the user left it on the repository default — the base git never sees.</summary>
    public string? BaseBranch => (BaseBox.SelectedItem as string) is { } selected && selected != RepositoryDefault
        ? selected
        : null;

    public CreatePullRequestSheet()
    {
        InitializeComponent();
    }

    public async Task LoadAsync(RepositoryViewModel viewModel)
    {
        _viewModel = viewModel;

        HeadText.Text = viewModel.CurrentBranch?.Name ?? "?";

        BaseBox.Items.Clear();
        BaseBox.Items.Add(RepositoryDefault);
        foreach (var name in viewModel.RemoteBranchNames) BaseBox.Items.Add(name);

        // Subscribed after the initial selection: it is not a choice the user made.
        BaseBox.SelectedIndex = 0;
        BaseBox.SelectionChanged += BaseBox_SelectionChanged;

        // Said before the push, not after it: opening a pull request publishes
        // the branch, and on a non-GitHub remote there is no page to open at all.
        if (PullRequestUrl.OwnerAndRepo(await viewModel.GetRemoteUrlAsync()) == null)
        {
            RemoteBar.Severity = InfoBarSeverity.Warning;
            RemoteBar.Title = "No GitHub remote";
            RemoteBar.Message = "\"origin\" does not point at GitHub, so there is no pull request page to open.";
        }
        else
        {
            RemoteBar.Title = "The branch is pushed first";
            RemoteBar.Message = "GitHub can only compare a branch it already has.";
        }

        RemoteBar.IsOpen = true;

        await ApplySuggestionAsync();

        // Selected, not just focused: the suggestion is a starting point, and
        // the first keystroke should be able to replace it.
        TitleBox.Focus(FocusState.Programmatic);
        TitleBox.SelectAll();
    }

    /// <summary>
    /// Fills the empty boxes with what the branch suggests. Changing the base
    /// changes which commits the pull request would carry, hence the answer —
    /// but only a box still holding the last suggestion is written to, so
    /// anything the user typed survives.
    /// </summary>
    private async Task ApplySuggestionAsync()
    {
        if (_viewModel == null) return;

        var draft = await _viewModel.SuggestPullRequestAsync(BaseBranch);

        if (TitleBox.Text == _suggestion.Title) TitleBox.Text = draft.Title;
        if (DescriptionBox.Text == _suggestion.Body) DescriptionBox.Text = draft.Body;

        _suggestion = draft;
        IsValidChanged?.Invoke(this, Title.Length > 0);
    }

    private async void BaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await ApplySuggestionAsync();

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) =>
        IsValidChanged?.Invoke(this, Title.Length > 0);
}
