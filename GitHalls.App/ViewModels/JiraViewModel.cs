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

    /// <summary>
    /// The moves already fetched, and the status they were fetched for. Which
    /// moves an issue can make is a function of where it stands, so a card that
    /// has moved has to ask again — hence the status alongside, and not just the
    /// key.
    /// </summary>
    private readonly Dictionary<string, (string Status, IReadOnlyList<JiraTransition> Transitions)> _transitions =
        new(StringComparer.Ordinal);

    /// <summary>Issues with a write in flight: one at a time each, and the board greys them.</summary>
    private readonly HashSet<string> _busyIssues = new(StringComparer.Ordinal);

    /// <summary>The account id an assign needs. The Task and not the value, so two windows asking at once still make one request.</summary>
    private Task<JiraAccount>? _myself;

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

    /// <summary>
    /// What the last write did, or why it didn't. Deliberately not
    /// <see cref="ErrorMessage"/>: that one is painted by the board's empty
    /// state, which is hidden whenever there are columns — precisely when a
    /// write happens, so an error routed there would never be seen.
    /// </summary>
    [ObservableProperty]
    private string? _actionMessage;

    [ObservableProperty]
    private bool _actionFailed;

    /// <summary>Which issue the message is about, so a window only shows the one that concerns it.</summary>
    [ObservableProperty]
    private string? _actionIssueKey;

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

    /// <summary>Issues a write is running on, so a card can show it and refuse a second click.</summary>
    public IReadOnlyCollection<string> BusyIssueKeys => _busyIssues;

    public bool IsIssueBusy(string key) => _busyIssues.Contains(key);

    /// <summary>The board's copy of an issue, for a window that wants to follow it.</summary>
    public JiraIssue? FindIssue(string key) =>
        _groups.SelectMany(g => g.Issues).FirstOrDefault(i => i.Key == key);

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

        _transitions.Clear();
        _myself = null;
        SetMyAccountId(null);

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

    public void ClearActionMessage()
    {
        ActionMessage = null;
        ActionFailed = false;
        ActionIssueKey = null;
    }

    private void ReportAction(string issueKey, string message, bool failed)
    {
        ActionIssueKey = issueKey;
        ActionFailed = failed;
        ActionMessage = message;
    }

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
            _transitions.Clear();

            // Learn who "me" is alongside the first search, so a card can tell
            // an issue that is already yours without stopping to ask.
            _ = WarmAccountAsync();
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
        return new JiraClient(RequireCredentials()).GetIssueAsync(key, cancellationToken);
    }

    // MARK: - Acting on an issue

    /// <summary>
    /// Who the credentials belong to. Asked once: /myself does not change under
    /// us. The Task is what is kept, so two windows asking at the same moment
    /// share one request — but a failed one is dropped, or a single flaky call
    /// would leave assigning broken for the rest of the session.
    /// </summary>
    public async Task<JiraAccount> MyselfAsync(CancellationToken cancellationToken = default)
    {
        var pending = _myself ??= new JiraClient(RequireCredentials()).MyselfAsync(cancellationToken);

        try
        {
            var me = await pending;
            SetMyAccountId(me.AccountId);
            return me;
        }
        catch
        {
            if (ReferenceEquals(_myself, pending)) _myself = null;
            throw;
        }
    }

    /// <summary>
    /// The account id, once something has asked for it. Null means "not asked
    /// yet", which is why a menu reads it rather than awaiting: a card can say
    /// "assign to me" without a request, and only say "already yours" when it
    /// actually knows.
    /// </summary>
    public string? MyAccountId { get; private set; }

    private void SetMyAccountId(string? accountId)
    {
        if (MyAccountId == accountId) return;

        MyAccountId = accountId;
        OnPropertyChanged(nameof(MyAccountId));
    }

    /// <summary>Asks who we are without anyone waiting on the answer; failing is not worth reporting.</summary>
    private async Task WarmAccountAsync()
    {
        if (MyAccountId != null) return;

        try
        {
            await MyselfAsync();
        }
        catch
        {
            // The search worked, so the credentials are fine; whatever went
            // wrong here only costs the "already yours" label.
        }
    }

    /// <summary>Assigns the issue to whoever the credentials belong to.</summary>
    public async Task<bool> AssignToMeAsync(JiraIssue issue)
    {
        JiraAccount me;
        try
        {
            me = await MyselfAsync();
        }
        catch (Exception ex)
        {
            ReportAction(issue.Key, ex.Message, failed: true);
            return false;
        }

        return await AssignIssueAsync(issue, me.AccountId, me.DisplayName);
    }

    /// <summary>
    /// The moves this issue can make. One request per menu, and the answer is
    /// kept only for as long as the issue stays where it was — a card that
    /// moved has a different set of moves, and a stale menu is worse than a
    /// second request.
    /// </summary>
    public async Task<IReadOnlyList<JiraTransition>> TransitionsForAsync(JiraIssue issue, CancellationToken cancellationToken = default)
    {
        if (_transitions.TryGetValue(issue.Key, out var cached) && cached.Status == issue.Status)
        {
            return cached.Transitions;
        }

        var transitions = await new JiraClient(RequireCredentials()).GetTransitionsAsync(issue.Key, cancellationToken);
        _transitions[issue.Key] = (issue.Status, transitions);
        return transitions;
    }

    /// <summary>Moves the issue, and moves the card to match. False when Jira refused.</summary>
    public Task<bool> MoveIssueAsync(JiraIssue issue, JiraTransition transition) =>
        WriteAsync(issue,
            (client, token) => client.TransitionAsync(issue.Key, transition.Id, token),
            current => current with { Status = transition.ToStatus, StatusCategory = transition.ToStatusCategory },
            $"{issue.Key} moved to {transition.ToStatus}.");

    /// <summary>Assigns the issue; a null account id unassigns it.</summary>
    public Task<bool> AssignIssueAsync(JiraIssue issue, string? accountId, string? displayName) =>
        WriteAsync(issue,
            (client, token) => client.AssignAsync(issue.Key, accountId, token),
            current => current with { AssigneeAccountId = accountId, AssigneeName = accountId == null ? null : displayName },
            accountId == null ? $"{issue.Key} unassigned." : $"{issue.Key} assigned to {displayName}.");

    /// <summary>
    /// The Jira half of starting work: assign it to yourself, then move it into
    /// progress. The branch is the window's job, and it goes first — see
    /// IssueWindow.
    /// </summary>
    /// <returns>What the workflow did, or null when Jira refused — in which case <see cref="ActionMessage"/> already says why.</returns>
    public async Task<JiraStartWork?> StartWorkJiraAsync(JiraIssue issue)
    {
        JiraAccount me;
        IReadOnlyList<JiraTransition> transitions;
        var latest = FindIssue(issue.Key) ?? issue;

        try
        {
            me = await MyselfAsync();
            transitions = await TransitionsForAsync(latest);
        }
        catch (Exception ex)
        {
            ReportAction(issue.Key, ex.Message, failed: true);
            return null;
        }

        if (latest.AssigneeAccountId != me.AccountId
            && !await AssignIssueAsync(latest, me.AccountId, me.DisplayName))
        {
            return null;
        }

        latest = FindIssue(issue.Key) ?? latest;

        var choice = JiraWorkflow.StartWork(transitions, latest.StatusCategory);
        if (choice.Outcome != JiraStartWork.Move) return choice.Outcome;

        return await MoveIssueAsync(latest, choice.Transition!) ? JiraStartWork.Move : null;
    }

    /// <summary>
    /// One write, with the busy flag, the error handling and the optimistic
    /// board update every one of them needs.
    /// </summary>
    private async Task<bool> WriteAsync(JiraIssue issue, Func<JiraClient, CancellationToken, Task> write,
                                        Func<JiraIssue, JiraIssue> apply, string confirmation)
    {
        // A second click on a card already being written is not a queue.
        if (!_busyIssues.Add(issue.Key)) return false;
        OnPropertyChanged(nameof(BusyIssueKeys));

        // Not a guard on the write — once sent, Jira has done it — but on
        // publishing the local patch: a search that lands meanwhile is the
        // newer truth, and this update must not be laid back over it.
        var token = _searchToken;

        try
        {
            await write(new JiraClient(RequireCredentials()), CancellationToken.None);

            _transitions.Remove(issue.Key);

            if (_searchToken == token) ReplaceIssue(issue.Key, apply);

            ReportAction(issue.Key, confirmation, failed: false);
            return true;
        }
        catch (Exception ex)
        {
            ReportAction(issue.Key, ex.Message, failed: true);
            return false;
        }
        finally
        {
            _busyIssues.Remove(issue.Key);
            OnPropertyChanged(nameof(BusyIssueKeys));
        }
    }

    /// <summary>
    /// Corrects the board in place rather than searching again. Jira's search
    /// index runs seconds behind a write, so a refetch here would hand back the
    /// status the card just left and bounce it home.
    ///
    /// The columns come from the issues present, so the last card leaving a
    /// status takes its column with it, and a card arriving at a status nobody
    /// held opens a new one. Both settle on the next refresh.
    /// </summary>
    private void ReplaceIssue(string key, Func<JiraIssue, JiraIssue> apply)
    {
        var issues = _groups.SelectMany(g => g.Issues).ToList();

        var index = issues.FindIndex(i => i.Key == key);
        if (index < 0) return;

        issues[index] = apply(issues[index]);
        _groups = JiraIssueGrouping.ByStatus(issues);

        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(IssueCount));
    }

    private JiraCredentials RequireCredentials() =>
        _account.Current ?? throw new JiraException(JiraFailure.Unauthorized, "Jira is not connected.");

    /// <summary>The branch this issue suggests. A starting point, always editable.</summary>
    public string SuggestedBranchName(JiraIssue issue) => JiraBranchName.Suggest(issue);

    public Uri? BrowseUrl(JiraIssue issue)
    {
        var credentials = _account.Current;
        return credentials == null ? null : new JiraClient(credentials).BrowseUrl(issue.Key);
    }
}
