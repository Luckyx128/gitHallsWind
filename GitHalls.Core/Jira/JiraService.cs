using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GitHalls.Core.Jira;

public class JiraService : IJiraService
{
    public Task<JiraAccount> MyselfAsync(JiraCredentials credentials, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).MyselfAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JiraIssue>> SearchAsync(JiraCredentials credentials, string jql, int limit = 50, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).SearchAsync(jql, limit, cancellationToken);
    }

    public Task<JiraIssue> GetIssueAsync(JiraCredentials credentials, string key, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetIssueAsync(key, cancellationToken);
    }

    public Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(JiraCredentials credentials, string key, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetTransitionsAsync(key, cancellationToken);
    }

    public Task TransitionAsync(JiraCredentials credentials, string key, string transitionId, JiraIssueUpdateParameters? fields = null, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).TransitionAsync(key, transitionId, fields, cancellationToken);
    }

    public Task AssignAsync(JiraCredentials credentials, string key, string? accountId, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).AssignAsync(key, accountId, cancellationToken);
    }

    public Task<string> CreateIssueAsync(JiraCredentials credentials, JiraIssueCreateParameters parameters, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).CreateIssueAsync(parameters, cancellationToken);
    }

    public Task UpdateIssueAsync(JiraCredentials credentials, string key, JiraIssueUpdateParameters parameters, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).UpdateIssueAsync(key, parameters, cancellationToken);
    }

    public Task<IReadOnlyList<JiraProject>> GetProjectsAsync(JiraCredentials credentials, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetProjectsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(JiraCredentials credentials, string projectKey, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetIssueTypesAsync(projectKey, cancellationToken);
    }

    public Task<IReadOnlyList<JiraCreateField>> GetCreateFieldsAsync(JiraCredentials credentials, string projectKey, string issueTypeId, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetCreateFieldsAsync(projectKey, issueTypeId, cancellationToken);
    }

    public Task<IReadOnlyList<JiraFieldOption>> FindTeamsAsync(JiraCredentials credentials, string query, string? autoCompleteUrl = null, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).FindTeamsAsync(query, autoCompleteUrl, cancellationToken);
    }

    public Task<IReadOnlyList<JiraSprint>> GetOpenSprintsAsync(JiraCredentials credentials, string projectKey, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).GetOpenSprintsAsync(projectKey, cancellationToken);
    }

    public Task MoveToSprintAsync(JiraCredentials credentials, int sprintId, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        return new JiraClient(credentials).MoveToSprintAsync(sprintId, keys, cancellationToken);
    }

    public Uri BrowseUrl(JiraCredentials credentials, string key)
    {
        return new JiraClient(credentials).BrowseUrl(key);
    }
}
