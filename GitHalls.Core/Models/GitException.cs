namespace GitHalls.Core.Models;

public class GitException : Exception
{
    public string RawError { get; }

    public GitException(string message, string rawError) : base(message)
    {
        RawError = rawError;
    }

    public static GitException Parse(string? rawError)
    {
        var error = rawError?.Trim() ?? string.Empty;
        var friendlyMessage = GetFriendlyMessage(error);
        return new GitException(friendlyMessage, error);
    }

    private static string GetFriendlyMessage(string rawError)
    {
        if (rawError.Contains("Authentication failed"))
            return "Authentication failed. Please check your credentials.";
        if (rawError.Contains("Repository not found") || rawError.Contains("not found"))
            return "Repository not found. It may be private or deleted.";
        if (rawError.Contains("Filename too long"))
            return "Path length limit exceeded. Enable long paths in Windows (core.longpaths=true).";
        if (string.IsNullOrEmpty(rawError))
            return "An unknown Git error occurred.";
        
        return rawError;
    }
}
