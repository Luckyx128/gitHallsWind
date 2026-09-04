using GitHalls.Core.Models;

namespace GitHalls.Core.Commits;

public class ConventionalCommitSuggester
{
    public string SuggestType(IReadOnlyList<FileChange> stagedChanges)
    {
        if (stagedChanges == null || stagedChanges.Count == 0) return "chore";

        // Logic to suggest type based on files changed. Simple port.
        var fileNames = stagedChanges.Select(c => c.FileName.ToLowerInvariant()).ToList();
        var paths = stagedChanges.Select(c => c.Path.ToLowerInvariant()).ToList();

        if (fileNames.Any(f => f.Contains("docker") || f.Contains("jenkins") || f.Contains(".github") || f.EndsWith(".yml")))
            return "ci";

        if (fileNames.Any(f => f.EndsWith(".md") || f.EndsWith(".txt")))
            return "docs";

        if (paths.Any(p => p.Contains("test") || p.Contains("spec")))
            return "test";

        if (fileNames.Any(f => f == "package.json" || f == "pom.xml" || f == "build.gradle" || f.EndsWith(".csproj")))
            return "build";

        // If it's just code changes without any clear indicators, default to feat/fix.
        // Returning empty or a default is fine for a suggestion.
        return "feat";
    }
}
