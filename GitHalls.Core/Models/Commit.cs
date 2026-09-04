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
}
