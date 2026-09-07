using System.Text.Json.Serialization;

namespace GitHalls.Core.Models;

/// <summary>
/// A saved identity: who you are on this project. Kept as a profile the user
/// names ("Work", "Personal") because the point is switching between them
/// without retyping a name and an email each time.
///
/// The GitHub username is separate from the email on purpose — it is what the
/// remote URL and the credential helper key off, and it is frequently not the
/// same string as either the name or the email.
/// </summary>
public class GitIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string GitHubUsername { get; set; } = string.Empty;

    /// <summary>What a list row shows: the label if it was given one, the name otherwise.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    public bool Matches(GitAuthor? author) =>
        author != null
        && string.Equals(author.Name, Name, StringComparison.Ordinal)
        && string.Equals(author.Email, Email, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The author git would actually record for a commit here — read from config,
/// so it may come from the repository, the global file, or the system one.
/// </summary>
public sealed record GitAuthor(string Name, string Email)
{
    public override string ToString() => $"{Name} <{Email}>";
}
