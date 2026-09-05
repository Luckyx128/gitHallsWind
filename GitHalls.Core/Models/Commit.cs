namespace GitHalls.Core.Models;

public class Commit
{
    public string Hash { get; }
    public string ShortHash { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public DateTimeOffset Date { get; }
    public string Message { get; }

    public Commit(string hash, string authorName, string authorEmail, DateTimeOffset date, string message)
    {
        Hash = hash;
        ShortHash = hash.Length >= 7 ? hash.Substring(0, 7) : hash;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        Date = date;
        Message = message;
    }

    /// <summary>First line of the message — what the history list shows.</summary>
    public string Summary
    {
        get
        {
            var newline = Message.IndexOf('\n');
            return newline < 0 ? Message : Message.Substring(0, newline);
        }
    }

    /// <summary>
    /// Coarse "2 hours ago" style label. Follows the same convention as
    /// FileChange's badge helpers: small presentation shortcuts live on the
    /// model so every view formats a commit the same way.
    /// </summary>
    public string RelativeDate
    {
        get
        {
            var elapsed = DateTimeOffset.Now - Date;

            if (elapsed.TotalSeconds < 60) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays} d ago";
            if (elapsed.TotalDays < 365) return Date.ToLocalTime().ToString("d MMM");

            return Date.ToLocalTime().ToString("d MMM yyyy");
        }
    }
}


/// <summary>A commit plus the diff of every file it touched.</summary>
public class CommitDetail
{
    public Commit Commit { get; }
    public IReadOnlyList<FileDiff> FileDiffs { get; }

    public CommitDetail(Commit commit, IReadOnlyList<FileDiff> fileDiffs)
    {
        Commit = commit;
        FileDiffs = fileDiffs;
    }
}
