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
}
