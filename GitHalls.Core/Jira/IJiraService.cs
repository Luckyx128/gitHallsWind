using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GitHalls.Core.Jira;

public interface IJiraService
{
    Task<JiraAccount> MyselfAsync(JiraCredentials credentials, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssue>> SearchAsync(JiraCredentials credentials, string jql, int limit = 50, CancellationToken cancellationToken = default);
    Task<JiraIssue> GetIssueAsync(JiraCredentials credentials, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(JiraCredentials credentials, string key, CancellationToken cancellationToken = default);
    Task TransitionAsync(JiraCredentials credentials, string key, string transitionId, JiraIssueUpdateParameters? fields = null, CancellationToken cancellationToken = default);
    Task AssignAsync(JiraCredentials credentials, string key, string? accountId, CancellationToken cancellationToken = default);
    
    Task<string> CreateIssueAsync(JiraCredentials credentials, JiraIssueCreateParameters parameters, CancellationToken cancellationToken = default);
    Task UpdateIssueAsync(JiraCredentials credentials, string key, JiraIssueUpdateParameters parameters, CancellationToken cancellationToken = default);
}
