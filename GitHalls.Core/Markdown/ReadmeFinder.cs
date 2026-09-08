namespace GitHalls.Core.Markdown;

/// <summary>
/// Which file in a repository's root is its README.
///
/// The name is a convention, not a rule: it comes in several spellings and any
/// casing. Picking is kept apart from reading so it can be tested without a
/// repository on disk.
/// </summary>
public static class ReadmeFinder
{
    /// <summary>
    /// Best first. A repository carrying both README.md and README.txt means
    /// the markdown one, which is what every forge shows.
    /// </summary>
    private static readonly string[] PreferredExtensions = { ".md", ".markdown", ".mdown", ".rst", ".txt", "" };

    public static string? Pick(IReadOnlyList<string> fileNames)
    {
        return fileNames
            .Where(name => Path.GetFileNameWithoutExtension(name).Equals("readme", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Rank)
            .ThenBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static int Rank(string name)
    {
        var extension = Path.GetExtension(name);
        var index = Array.FindIndex(PreferredExtensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? PreferredExtensions.Length : index;
    }
}
