namespace GitHalls.Core.Git;

/// <summary>
/// An editor this machine can hand a file to.
///
/// Found two ways, in order: the launcher on PATH, which is what a user who
/// works from a terminal already has, and then the places installers put the
/// executable. A name that matches neither costs one missing menu entry and
/// nothing else — which is what makes guessing at the list safe.
/// </summary>
public sealed record ExternalEditor(string Name, string Command, IReadOnlyList<string> InstallPaths);

public static class ExternalEditors
{
    public static IReadOnlyList<ExternalEditor> Known { get; } = new[]
    {
        new ExternalEditor("Visual Studio Code", "code", new[]
        {
            @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe",
            @"%ProgramFiles%\Microsoft VS Code\Code.exe"
        }),
        new ExternalEditor("VS Code Insiders", "code-insiders", new[]
        {
            @"%LOCALAPPDATA%\Programs\Microsoft VS Code Insiders\Code - Insiders.exe"
        }),
        new ExternalEditor("Cursor", "cursor", new[]
        {
            @"%LOCALAPPDATA%\Programs\cursor\Cursor.exe"
        }),
        new ExternalEditor("Zed", "zed", new[]
        {
            @"%LOCALAPPDATA%\Programs\Zed\Zed.exe"
        }),
        new ExternalEditor("WebStorm", "webstorm", Array.Empty<string>()),
        new ExternalEditor("IntelliJ IDEA", "idea", Array.Empty<string>()),
        new ExternalEditor("PyCharm", "pycharm", Array.Empty<string>()),
        new ExternalEditor("PhpStorm", "phpstorm", Array.Empty<string>()),
        new ExternalEditor("GoLand", "goland", Array.Empty<string>()),
        new ExternalEditor("Rider", "rider", Array.Empty<string>()),
        new ExternalEditor("Sublime Text", "subl", new[]
        {
            @"%ProgramFiles%\Sublime Text\sublime_text.exe"
        }),
        new ExternalEditor("Notepad++", "notepad++", new[]
        {
            @"%ProgramFiles%\Notepad++\notepad++.exe",
            @"%ProgramFiles(x86)%\Notepad++\notepad++.exe"
        })
    };

    /// <summary>
    /// The executable to launch, or null when this editor is not installed.
    /// JetBrains ships its launchers through Toolbox, which puts them on PATH
    /// and nowhere predictable — hence the empty path lists above.
    /// </summary>
    public static string? Locate(ExternalEditor editor)
    {
        var onPath = FindOnPath(editor.Command);
        if (onPath != null) return onPath;

        foreach (var candidate in editor.InstallPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);

            // An unset variable expands to itself, which would turn into a
            // nonsense path rather than a miss.
            if (expanded.Contains('%')) continue;
            if (File.Exists(expanded)) return expanded;
        }

        return null;
    }

    public static IReadOnlyList<ExternalEditor> Installed() =>
        Known.Where(editor => Locate(editor) != null).ToList();

    /// <summary>
    /// Walks PATH rather than shelling out to "where": one process per menu is
    /// a poor trade for a directory listing.
    /// </summary>
    private static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim(), command + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not worth failing the lookup for.
                    break;
                }

                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
