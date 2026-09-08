namespace GitHalls.Core.GitHub;

using System;
using System.Collections.Generic;
using GitHalls.Core.Models;

/// <summary>
/// What a pull request opens with, filled in the way GitHub itself fills it: a
/// branch carrying one commit is that commit, so its message is the whole
/// proposal; a branch carrying several is a piece of work with no single
/// message, and its name is the only thing that describes all of them.
///
/// A suggestion, not a rule — the user types over it before anything is
/// created.
/// </summary>
public readonly record struct PullRequestDraft(string Title, string Body)
{
    public static readonly PullRequestDraft Empty = new(string.Empty, string.Empty);

    /// <summary>Branch-name prefixes that say what the work is, not what it does.</summary>
    private static readonly string[] TypePrefixes =
    {
        "feature", "feat", "fix", "bugfix", "hotfix", "bug", "chore", "docs", "doc",
        "refactor", "release", "test", "tests", "spike", "task", "story", "ci",
        "build", "perf", "style",
    };

    /// <summary><paramref name="commits"/> are the ones the base branch does not have, newest first.</summary>
    public static PullRequestDraft From(IReadOnlyList<Commit> commits, string? branchName)
    {
        if (commits.Count == 1)
        {
            var only = commits[0];
            return new PullRequestDraft(only.Summary.Trim(), BodyOf(only));
        }

        return new PullRequestDraft(TitleFromBranch(branchName), string.Empty);
    }

    /// <summary>Everything after the subject line — the body git itself treats as the explanation.</summary>
    private static string BodyOf(Commit commit)
    {
        var newline = commit.Message.IndexOf('\n');
        return newline < 0 ? string.Empty : commit.Message[(newline + 1)..].Trim();
    }

    /// <summary>
    /// Reads a branch name as a sentence: "feature-SWEB-12903" is "SWEB-12903",
    /// "fix/login-crash" is "Login crash".
    ///
    /// An issue key is kept exactly as it is spelled — it is the one part of the
    /// name people search for, and prettifying it into "Sweb 12903" would lose
    /// the thing the title is for.
    /// </summary>
    public static string TitleFromBranch(string? branchName)
    {
        var name = (branchName ?? string.Empty).Trim().Trim('/', '-', '_');
        if (name.Length == 0) return string.Empty;

        var words = name.Split(new[] { '/', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        var start = 0;
        if (words.Length > 1 && Array.Exists(TypePrefixes, prefix => prefix.Equals(words[0], StringComparison.OrdinalIgnoreCase)))
        {
            start = 1;
        }

        if (IsIssueKey(words, start))
        {
            var key = $"{words[start]}-{words[start + 1]}";
            var rest = string.Join(' ', words[(start + 2)..]);
            return rest.Length == 0 ? key : $"{key}: {rest}";
        }

        var sentence = string.Join(' ', words[start..]);
        if (sentence.Length == 0) return string.Empty;

        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>
    /// "SWEB" followed by "12903". Upper case is what makes it a key and not two
    /// words: "add-2fa" splits the same way, and "ADD-2" is not what that branch
    /// is called.
    /// </summary>
    private static bool IsIssueKey(string[] words, int start)
    {
        if (start + 1 >= words.Length) return false;

        var project = words[start];
        if (project.Length < 2 || !char.IsAsciiLetterUpper(project[0])) return false;
        foreach (var character in project)
        {
            if (!char.IsAsciiLetterUpper(character) && !char.IsAsciiDigit(character)) return false;
        }

        var number = words[start + 1];
        if (number.Length == 0) return false;
        foreach (var character in number)
        {
            if (!char.IsAsciiDigit(character)) return false;
        }

        return true;
    }
}
