namespace GitHalls.Core.Models;

public class Commit
{
    public string Hash { get; }
    public string ShortHash { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public DateTimeOffset Date { get; }
    public string Message { get; }
    public IReadOnlyList<string> Parents { get; }
    public IReadOnlyList<string> Refs { get; }
    public bool IsPendingPush { get; set; }
    public string? Url { get; set; }

    public Commit(string hash, string authorName, string authorEmail, DateTimeOffset date, string message, IReadOnlyList<string>? parents = null, IReadOnlyList<string>? refs = null, bool isPendingPush = false, string? url = null)
    {
        Hash = hash;
        ShortHash = hash.Length >= 7 ? hash.Substring(0, 7) : hash;
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        Date = date;
        Message = message;
        Parents = parents ?? Array.Empty<string>();
        Refs = refs ?? Array.Empty<string>();
        IsPendingPush = isPendingPush;
        Url = url;
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

    /// <summary>
    /// Co-authors extracted from "Co-authored-by: Name <email>" trailers in the commit message.
    /// </summary>
    public IReadOnlyList<string> CoAuthors
    {
        get
        {
            if (string.IsNullOrEmpty(Message)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var line in Message.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Co-authored-by:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = trimmed.Substring("Co-authored-by:".Length).Trim();
                    if (!string.IsNullOrEmpty(val))
                    {
                        list.Add(val);
                    }
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Displays Author Name and co-authors if present (e.g., "Lucas (+Helix, Prism)").
    /// </summary>
    public string AuthorsDisplay
    {
        get
        {
            if (CoAuthors.Count == 0) return AuthorName;
            var names = CoAuthors.Select(a =>
            {
                var idx = a.IndexOf('<');
                return idx > 0 ? a.Substring(0, idx).Trim() : a.Trim();
            });
            return $"{AuthorName} (+{string.Join(", ", names)})";
        }
    }
}
