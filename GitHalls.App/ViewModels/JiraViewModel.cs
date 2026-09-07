using CommunityToolkit.Mvvm.ComponentModel;
using GitHalls.App.Services;
using GitHalls.Core.Jira;
using System.Collections.ObjectModel;

namespace GitHalls.App.ViewModels;

/// <summary>
/// Jira state, kept deliberately apart from <see cref="RepositoryViewModel"/>:
/// Jira is not git, and the repository view model already owns enough. The two
/// meet in exactly one place — the issue detail pane, which holds both and asks
/// the repository to create a branch.
/// </summary>
public partial class JiraViewModel : ObservableObject
{
    /// <summary>What a developer wants to see first: their own open work.</summary>
    public const string DefaultJql = "assignee = currentUser() ORDER BY updated DESC";

    private readonly JiraAccountStore _account;

    /// <summary>Same guard the git loads use: a slow answer to a query the user
    /// has moved on from must not replace what is on screen.</summary>
    private Guid _searchToken;

    [ObservableProperty]
    private string _jql = DefaultJql;

    [ObservableProperty]
    private JiraIssue? _selectedIssue;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>True once a search has run and returned nothing — not before.</summary>
    [ObservableProperty]
    private bool _hasSearched;

    public JiraViewModel(JiraAccountStore account)
    {
        _account = account;
    }

    public ObservableCollection<JiraIssueGroup> Groups { get; } = new();

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsConfigured => _account.IsConfigured;

    /// <summary>Re-read after the settings screen connects or disconnects an account.</summary>
    public void NotifyAccountChanged()
    {
        OnPropertyChanged(nameof(IsConfigured));

        if (!IsConfigured)
        {
            Groups.Clear();
            SelectedIssue = null;
            HasSearched = false;
        }
    }

    public void ClearError() => ErrorMessage = null;

    public async Task RefreshAsync()
    {
        var credentials = _account.Current;
        if (credentials == null)
        {
            Groups.Clear();
            SelectedIssue = null;
            ErrorMessage = "Jira is not connected. Open Settings to connect an account.";
            return;
        }

        var token = Guid.NewGuid();
        _searchToken = token;
        IsLoading = true;

        try
        {
            var issues = await new JiraClient(credentials).SearchAsync(Jql);

            if (_searchToken != token) return;

            // The selection is restored by key: the same issue comes back as a
            // different object every search.
            var selectedKey = SelectedIssue?.Key;

            Groups.Clear();
            foreach (var group in JiraIssueGrouping.ByStatus(issues)) Groups.Add(group);

            SelectedIssue = selectedKey == null
                ? null
                : issues.FirstOrDefault(i => string.Equals(i.Key, selectedKey, StringComparison.Ordinal));

            HasSearched = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (_searchToken != token) return;

            // JiraException already carries a sentence written for a person;
            // anything else surfaces as whatever .NET said.
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (_searchToken == token) IsLoading = false;
        }
    }

    /// <summary>The branch this issue suggests. A starting point, always editable.</summary>
    public string SuggestedBranchName(JiraIssue issue) => JiraBranchName.Suggest(issue);

    public Uri? BrowseUrl(JiraIssue issue)
    {
        var credentials = _account.Current;
        return credentials == null ? null : new JiraClient(credentials).BrowseUrl(issue.Key);
    }
}
