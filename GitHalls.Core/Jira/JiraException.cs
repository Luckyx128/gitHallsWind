namespace GitHalls.Core.Jira;

public enum JiraFailure
{
    /// <summary>Jira refused the request (401/403): bad credentials, or a permission the account lacks.</summary>
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

    /// <summary>
    /// 401 and 403 arrive here together. Saying only "bad credentials" was
    /// close enough while everything was a read; a write gets 403 from a token
    /// that reads the project fine but may not move or assign in it, and
    /// telling that person their token is broken sends them to fix the wrong
    /// thing.
    /// </summary>
    public static JiraException Unauthorized() =>
        new(JiraFailure.Unauthorized, "Jira refused the request \u2014 check the credentials, or your permissions on this project.", 401);

    public static JiraException RateLimited(TimeSpan retryAfter) =>
        new(JiraFailure.RateLimited, "Jira asked to slow down (rate limited).", 429, retryAfter);

    public static JiraException Http(int status, string? message) =>
        new(JiraFailure.Http, message ?? $"Jira responded with {status}.", status);

    public static JiraException Malformed() =>
        new(JiraFailure.MalformedResponse, "Jira response was in an unexpected format.");
}
