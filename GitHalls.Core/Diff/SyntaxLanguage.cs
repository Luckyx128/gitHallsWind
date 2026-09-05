namespace GitHalls.Core.Diff;

/// <summary>
/// Maps a file path to a language identifier for syntax highlighting.
///
/// The ids are highlight.js-style names, deliberately neutral: this lives in
/// Core, so it can't depend on whichever highlighting library the UI happens to
/// use. The presentation layer translates an id to its own grammar and falls
/// back to plain text when it has none.
/// </summary>
public static class SyntaxLanguage
{
    /// <summary>Language id for <paramref name="path"/>, or null when the name/extension is unknown.</summary>
    public static string? ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var name = System.IO.Path.GetFileName(path.Replace('\\', '/'));
        if (string.IsNullOrEmpty(name)) return null;

        if (ByBasename.TryGetValue(name, out var byName)) return byName;

        var extension = System.IO.Path.GetExtension(name);
        if (string.IsNullOrEmpty(extension)) return null;

        return ByExtension.TryGetValue(extension.Substring(1), out var byExtension) ? byExtension : null;
    }

    private static readonly Dictionary<string, string> ByBasename = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = "dockerfile",
        ["Makefile"] = "makefile",
        ["GNUmakefile"] = "makefile",
        ["CMakeLists.txt"] = "cmake",
        ["Package.swift"] = "swift",
        ["Podfile"] = "ruby",
        ["Fastfile"] = "ruby",
        ["Gemfile"] = "ruby",
        ["Rakefile"] = "ruby",
        [".gitconfig"] = "ini",
        [".gitignore"] = "ini",
        [".editorconfig"] = "ini",
        ["Directory.Build.props"] = "xml",
        ["Directory.Build.targets"] = "xml",
    };

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET / Windows
        ["cs"] = "csharp",
        ["csx"] = "csharp",
        ["vb"] = "vbnet",
        ["fs"] = "fsharp",
        ["fsx"] = "fsharp",
        ["xaml"] = "xml",
        ["csproj"] = "xml",
        ["vbproj"] = "xml",
        ["fsproj"] = "xml",
        ["props"] = "xml",
        ["targets"] = "xml",
        ["slnx"] = "xml",
        ["nuspec"] = "xml",
        ["resx"] = "xml",
        ["appxmanifest"] = "xml",
        ["pubxml"] = "xml",
        ["razor"] = "xml",
        ["cshtml"] = "xml",
        ["ps1"] = "powershell",
        ["psm1"] = "powershell",
        ["psd1"] = "powershell",
        ["bat"] = "dos",
        ["cmd"] = "dos",

        // Apple / native
        ["swift"] = "swift",
        ["m"] = "objectivec",
        ["mm"] = "objectivec",
        ["h"] = "objectivec",
        ["hpp"] = "cpp",
        ["hh"] = "cpp",
        ["c"] = "c",
        ["cc"] = "cpp",
        ["cpp"] = "cpp",
        ["cxx"] = "cpp",

        // Web
        ["js"] = "javascript",
        ["jsx"] = "javascript",
        ["mjs"] = "javascript",
        ["cjs"] = "javascript",
        ["ts"] = "typescript",
        ["tsx"] = "typescript",
        ["css"] = "css",
        ["scss"] = "scss",
        ["sass"] = "scss",
        ["less"] = "less",
        ["html"] = "xml",
        ["htm"] = "xml",
        ["xhtml"] = "xml",
        ["svg"] = "xml",
        ["vue"] = "xml",

        // Other languages
        ["py"] = "python",
        ["pyi"] = "python",
        ["rb"] = "ruby",
        ["go"] = "go",
        ["rs"] = "rust",
        ["java"] = "java",
        ["kt"] = "kotlin",
        ["kts"] = "kotlin",
        ["php"] = "php",
        ["pl"] = "perl",
        ["pm"] = "perl",
        ["lua"] = "lua",
        ["r"] = "r",
        ["scala"] = "scala",
        ["clj"] = "clojure",
        ["ex"] = "elixir",
        ["exs"] = "elixir",
        ["erl"] = "erlang",
        ["hs"] = "haskell",
        ["dart"] = "dart",
        ["groovy"] = "groovy",
        ["gradle"] = "groovy",

        // Data / config / markup
        ["json"] = "json",
        ["json5"] = "json",
        ["yaml"] = "yaml",
        ["yml"] = "yaml",
        ["toml"] = "ini",
        ["ini"] = "ini",
        ["cfg"] = "ini",
        ["conf"] = "ini",
        ["xml"] = "xml",
        ["plist"] = "xml",
        ["md"] = "markdown",
        ["markdown"] = "markdown",
        ["sh"] = "bash",
        ["bash"] = "bash",
        ["zsh"] = "bash",
        ["fish"] = "bash",
        ["sql"] = "sql",
        ["graphql"] = "graphql",
        ["gql"] = "graphql",
        ["proto"] = "protobuf",
        ["dockerfile"] = "dockerfile",
        ["cmake"] = "cmake",
        ["make"] = "makefile",
        ["mk"] = "makefile",
        ["diff"] = "diff",
        ["patch"] = "diff",
    };
}
