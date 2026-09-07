namespace GitHalls.Core.Jira;

public enum JiraFailure
{
    /// <summary>Jira rejected the credentials (401/403).</summary>
    Unauthorized,
    /// <summary>Jira asked to slow down (429).</summary>
    RateLimited,
    /// <summary>Any other HTTP failure, with whatever Jira said about it.</summary>
    Http,
    /// <summary>A 2xx whose body wasn't the shape the API documents.</summary>
    MalformedResponse,
    /// <summary>The request never completed: no network, DNS, TLS, timeout.</summary>
    Unreachable
}

public sealed class JiraException : Exception
{
    public JiraFailure Failure { get; }
    public int? StatusCode { get; }

    /// <summary>How long Jira asked us to wait, when it said so.</summary>
    public TimeSpan? RetryAfter { get; }

    public JiraException(JiraFailure failure, string message, int? statusCode = null,
                         TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public static JiraException Unauthorized() =>
        new(JiraFailure.Unauthorized, "Jira rejected the credentials.", 401);

    public static JiraException RateLimited(TimeSpan retryAfter) =>
        new(JiraFailure.RateLimited, "Jira asked to slow down (rate limited).", 429, retryAfter);

    public static JiraException Http(int status, string? message) =>
        new(JiraFailure.Http, message ?? $"Jira responded with {status}.", status);

    public static JiraException Malformed() =>
        new(JiraFailure.MalformedResponse, "Jira response was in an unexpected format.");
}
