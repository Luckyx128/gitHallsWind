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
    Task<IReadOnlyList<JiraProject>> GetProjectsAsync(JiraCredentials credentials, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssueType>> GetIssueTypesAsync(JiraCredentials credentials, string projectKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraCreateField>> GetCreateFieldsAsync(JiraCredentials credentials, string projectKey, string issueTypeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraFieldOption>> FindTeamsAsync(JiraCredentials credentials, string query, string? autoCompleteUrl = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraSprint>> GetOpenSprintsAsync(JiraCredentials credentials, string projectKey, CancellationToken cancellationToken = default);
    Task MoveToSprintAsync(JiraCredentials credentials, int sprintId, IEnumerable<string> keys, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraComment>> GetCommentsAsync(JiraCredentials credentials, string issueKey, CancellationToken cancellationToken = default);
    Task<JiraComment> AddCommentAsync(JiraCredentials credentials, string issueKey, string bodyText, CancellationToken cancellationToken = default);
    Task<JiraComment> UpdateCommentAsync(JiraCredentials credentials, string issueKey, string commentId, string bodyText, CancellationToken cancellationToken = default);
    Task DeleteCommentAsync(JiraCredentials credentials, string issueKey, string commentId, CancellationToken cancellationToken = default);
    Uri BrowseUrl(JiraCredentials credentials, string key);
}
