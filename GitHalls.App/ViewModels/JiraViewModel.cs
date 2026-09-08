using CommunityToolkit.Mvvm.ComponentModel;
using GitHalls.App.Services;
using GitHalls.Core.Jira;

namespace GitHalls.App.ViewModels;

/// <summary>
/// Jira state, kept deliberately apart from <see cref="RepositoryViewModel"/>:
/// Jira is not git, and the repository view model already owns enough. The two
/// meet in exactly one place — the issue window, which holds both and asks the
/// repository to create a branch.
///
/// The board is one query at a time: a preset or one the user saved. The
/// result is grouped into columns by status, and a text filter narrows the
/// cards without asking Jira again.
/// </summary>
public partial class JiraViewModel : ObservableObject
{
    /// <summary>A sprint fits; a backlog does not, and shouldn't be a board.</summary>
    private const int SearchLimit = 100;

    private readonly JiraAccountStore _account;
    private readonly SettingsStore _settings;

    /// <summary>Same guard the git loads use: a slow answer to a query the user
    /// has moved on from must not replace what is on screen.</summary>
    private Guid _searchToken;

    /// <summary>The last result, unfiltered, so a filter change costs no request.</summary>
    private IReadOnlyList<JiraIssueGroup> _groups = Array.Empty<JiraIssueGroup>();

    private List<JiraQuery> _queries = new(JiraQueryPresets.All);

    [ObservableProperty]
    private JiraQuery _selectedQuery = JiraQueryPresets.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Columns))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>True once a search has run and returned — not before.</summary>
    [ObservableProperty]
    private bool _hasSearched;

    public JiraViewModel(JiraAccountStore account, SettingsStore settings)
    {
        _account = account;
        _settings = settings;
    }

    /// <summary>Presets first, then the user's own, in the order they were saved.</summary>
    public IReadOnlyList<JiraQuery> Queries => _queries;

    /// <summary>The board: one column per status, holding only the cards that pass the filter.</summary>
    public IReadOnlyList<JiraIssueGroup> Columns => JiraIssueGrouping.Filter(_groups, FilterText);

    /// <summary>How many cards the query returned, before any filter.</summary>
    public int IssueCount => _groups.Sum(g => g.Count);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsConfigured => _account.IsConfigured;

    // MARK: - Lifecycle

    /// <summary>
    /// Reads the saved queries and the last selection. Called once the settings
    /// file is in — before that there is nothing to read, and the presets serve.
    /// </summary>
    public void LoadSavedQueries()
    {
        var settings = _settings.Current;
        _queries = new List<JiraQuery>(JiraQueryPresets.Combine(settings.JiraCustomQueries));
        OnPropertyChanged(nameof(Queries));

        var remembered = _queries.FirstOrDefault(q => q.Id == settings.JiraSelectedQueryId);
        if (remembered != null && !ReferenceEquals(remembered, SelectedQuery))
        {
            _suppressSelectionRefresh = true;
            try { SelectedQuery = remembered; }
            finally { _suppressSelectionRefresh = false; }
        }
    }

    /// <summary>Re-read after the settings screen connects or disconnects an account.</summary>
    public void NotifyAccountChanged()
    {
        OnPropertyChanged(nameof(IsConfigured));

        if (!IsConfigured)
        {
            _groups = Array.Empty<JiraIssueGroup>();
            OnPropertyChanged(nameof(Columns));
            OnPropertyChanged(nameof(IssueCount));
            HasSearched = false;
            ErrorMessage = null;
        }
    }

    public void ClearError() => ErrorMessage = null;

    // MARK: - Queries

    /// <summary>Set while a selection is restored from disk, so it doesn't fire a search of its own.</summary>
    private bool _suppressSelectionRefresh;

    partial void OnSelectedQueryChanged(JiraQuery value)
    {
        _ = _settings.UpdateAsync(s => s.JiraSelectedQueryId = value.Id);

        if (_suppressSelectionRefresh || !IsConfigured) return;
        _ = RefreshAsync();
    }

    public async Task AddQueryAsync(string name, string jql)
    {
        var query = new JiraQuery { Name = name.Trim(), Jql = jql.Trim() };
        _queries.Add(query);
        OnPropertyChanged(nameof(Queries));

        await _settings.UpdateAsync(s => s.JiraCustomQueries.Add(query));

        SelectedQuery = query;
    }

    public async Task UpdateQueryAsync(JiraQuery query, string name, string jql)
    {
        if (query.IsBuiltIn) return;

        query.Name = name.Trim();
        query.Jql = jql.Trim();
        OnPropertyChanged(nameof(Queries));

        // The saved copy is the same object as the listed one — Combine keeps
        // the instances the settings hold — so the file just needs writing.
        await _settings.UpdateAsync(_ => { });

        if (ReferenceEquals(query, SelectedQuery)) await RefreshAsync();
    }

    public async Task RemoveQueryAsync(JiraQuery query)
    {
        if (query.IsBuiltIn || !_queries.Remove(query)) return;
        OnPropertyChanged(nameof(Queries));

        await _settings.UpdateAsync(s => s.JiraCustomQueries.RemoveAll(q => q.Id == query.Id));

        if (ReferenceEquals(query, SelectedQuery)) SelectedQuery = JiraQueryPresets.Default;
    }

    // MARK: - Search

    public async Task RefreshAsync()
    {
        var credentials = _account.Current;
        if (credentials == null)
        {
            ErrorMessage = "Jira is not connected. Open Settings to connect an account.";
            return;
        }

        var token = Guid.NewGuid();
        _searchToken = token;
        IsLoading = true;

        try
        {
            var issues = await new JiraClient(credentials).SearchAsync(SelectedQuery.Jql, SearchLimit);

            if (_searchToken != token) return;

            _groups = JiraIssueGrouping.ByStatus(issues);
            OnPropertyChanged(nameof(Columns));
            OnPropertyChanged(nameof(IssueCount));

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

    // MARK: - One issue

    /// <summary>The full issue, description included. The window that asked owns the answer.</summary>
    public Task<JiraIssue> FetchIssueAsync(string key, CancellationToken cancellationToken = default)
    {
        var credentials = _account.Current
            ?? throw new JiraException(JiraFailure.Unauthorized, "Jira is not connected.");

        return new JiraClient(credentials).GetIssueAsync(key, cancellationToken);
    }

    /// <summary>The branch this issue suggests. A starting point, always editable.</summary>
    public string SuggestedBranchName(JiraIssue issue) => JiraBranchName.Suggest(issue);

    public Uri? BrowseUrl(JiraIssue issue)
    {
        var credentials = _account.Current;
        return credentials == null ? null : new JiraClient(credentials).BrowseUrl(issue.Key);
    }
}
