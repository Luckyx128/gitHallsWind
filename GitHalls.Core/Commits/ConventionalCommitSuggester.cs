using GitHalls.Core.Models;

namespace GitHalls.Core.Commits;

/// <summary>
/// Guesses a Conventional Commits type and scope from what is staged.
/// Port of ConventionalCommitSuggester.swift.
///
/// Every rule is "all staged files agree", never "any file matches": a commit
/// that touches a test and a feature is a feature, not a test.
/// </summary>
public static class ConventionalCommitSuggester
{
    /// <summary>Per-path staged line counts, as reported by <c>git diff --cached --numstat</c>.</summary>
    public readonly record struct LineStats(int Additions, int Deletions);

    public static ConventionalCommitType SuggestType(
        IReadOnlyList<FileChange> stagedChanges,
        IReadOnlyDictionary<string, LineStats>? numstat = null)
    {
        if (stagedChanges == null || stagedChanges.Count == 0) return ConventionalCommitType.Chore;

        if (stagedChanges.All(c => IsTestPath(c.Path))) return ConventionalCommitType.Test;
        if (stagedChanges.All(c => HasExtension(c.Path, ".md", ".txt", ".rst", ".adoc"))) return ConventionalCommitType.Docs;

        // Not in the Swift version, but a workflow or project file says more
        // about the commit than "these files were all added" does.
        if (stagedChanges.All(IsCiPath)) return ConventionalCommitType.Ci;
        if (stagedChanges.All(c => IsBuildPath(c.Path))) return ConventionalCommitType.Build;

        if (stagedChanges.All(c => c.IndexStatus is FileChangeStatus.Added or FileChangeStatus.Untracked)) return ConventionalCommitType.Feat;
        if (stagedChanges.All(c => c.IndexStatus == FileChangeStatus.Deleted)) return ConventionalCommitType.Chore;
        if (stagedChanges.All(c => c.IndexStatus is FileChangeStatus.Renamed or FileChangeStatus.Copied)) return ConventionalCommitType.Refactor;

        var additions = 0;
        var deletions = 0;
        if (numstat != null)
        {
            foreach (var change in stagedChanges)
            {
                if (!numstat.TryGetValue(change.Path, out var stats)) continue;
                additions += stats.Additions;
                deletions += stats.Deletions;
            }
        }

        return SuggestFromLineCounts(additions, deletions);
    }

    /// <summary>
    /// Shape of the edit as a proxy for its intent: mostly-new lines reads as a
    /// feature, a large two-way edit as a refactor, and a small one as a fix.
    /// </summary>
    private static ConventionalCommitType SuggestFromLineCounts(int additions, int deletions)
    {
        var total = additions + deletions;
        if (total == 0) return ConventionalCommitType.Chore;

        if (deletions == 0 || additions > deletions * 3) return ConventionalCommitType.Feat;
        if (total > 20 && additions > 0 && deletions > 0) return ConventionalCommitType.Refactor;

        return ConventionalCommitType.Fix;
    }

    /// <summary>
    /// The single top-level folder every staged file lives under, lowercased —
    /// or null when they are spread across more than one.
    /// </summary>
    public static string? SuggestScope(IReadOnlyList<FileChange> stagedChanges)
    {
        if (stagedChanges == null || stagedChanges.Count == 0) return null;

        var folders = stagedChanges
            .Select(c => c.Path.Replace('\\', '/').Split('/')[0])
            .Where(segment => segment.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return folders.Count == 1 ? folders[0].ToLowerInvariant() : null;
    }

    private static string[] Segments(string path) => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool HasExtension(string path, params string[] extensions)
        => extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    private static bool IsTestPath(string path)
    {
        var segments = Segments(path);
        if (segments.Length == 0) return false;

        // A directory anywhere in the path named test/tests, or ending in Tests
        // (the .NET convention: GitHalls.Core.Tests).
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment.Equals("test", StringComparison.OrdinalIgnoreCase)) return true;
            if (segment.Equals("tests", StringComparison.OrdinalIgnoreCase)) return true;
            if (segment.Equals("spec", StringComparison.OrdinalIgnoreCase)) return true;
            if (segment.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)) return true;
        }

        var fileName = segments[^1];
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);

        return stem.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("Spec", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCiPath(FileChange change)
    {
        var path = change.Path.Replace('\\', '/');
        if (path.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains(".gitlab-ci", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains("azure-pipelines", StringComparison.OrdinalIgnoreCase)) return true;

        var fileName = System.IO.Path.GetFileName(path);
        return fileName.Equals("Jenkinsfile", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("docker-compose.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuildPath(string path)
    {
        var fileName = System.IO.Path.GetFileName(path.Replace('\\', '/'));

        return HasExtension(fileName, ".csproj", ".vbproj", ".fsproj", ".sln", ".slnx", ".props", ".targets", ".nuspec")
            || fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase);
    }
}
