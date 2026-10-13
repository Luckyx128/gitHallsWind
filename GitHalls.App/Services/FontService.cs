using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace GitHalls.App.Services;

/// <summary>
/// Fast and safe enumeration of fonts installed in Windows via Registry (HKLM and HKCU).
/// Prioritizes monospaced fonts suitable for code diffs at the top.
/// </summary>
public static class FontService
{
    private static readonly string[] MonospacedPriorities =
    [
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "JetBrains Mono",
        "Fira Code",
        "Source Code Pro",
        "Courier New",
        "Inconsolata",
        "DejaVu Sans Mono",
        "Lucida Console",
        "Hack",
        "Ubuntu Mono"
    ];

    private static readonly Regex TypeSuffixRegex = new(@"\s*\([^)]*\)$", RegexOptions.Compiled);
    private static readonly Regex StyleSuffixRegex = new(@"\s+(Regular|Bold|Italic|Light|SemiBold|Semibold|Medium|Black|Thin|ExtraLight|ExtraBold|Extra|Oblique)\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IReadOnlyList<string>? _cachedFonts;
    private static readonly object _syncLock = new();

    public static IReadOnlyList<string> GetInstalledFonts()
    {
        if (_cachedFonts != null)
        {
            return _cachedFonts;
        }

        lock (_syncLock)
        {
            if (_cachedFonts != null)
            {
                return _cachedFonts;
            }

            var fontFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Read from HKLM and HKCU
            ReadFontsFromKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", fontFamilies);
            ReadFontsFromKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", fontFamilies);

            var prioritized = new List<string>();
            var others = new List<string>();

            // Prioritize popular monospaced fonts found on the system
            foreach (var mono in MonospacedPriorities)
            {
                if (fontFamilies.Remove(mono))
                {
                    prioritized.Add(mono);
                }
            }

            // Always ensure the standard Windows monospaced fonts are included in prioritized list
            if (!prioritized.Contains("Cascadia Mono", StringComparer.OrdinalIgnoreCase))
                prioritized.Insert(0, "Cascadia Mono");
            if (!prioritized.Contains("Cascadia Code", StringComparer.OrdinalIgnoreCase))
                prioritized.Insert(1, "Cascadia Code");
            if (!prioritized.Contains("Consolas", StringComparer.OrdinalIgnoreCase))
                prioritized.Insert(2, "Consolas");

            // Look for any other font whose name contains "Mono" or "Code"
            var additionalMono = fontFamilies
                .Where(f => f.Contains("Mono", StringComparison.OrdinalIgnoreCase) || f.Contains("Code", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var mono in additionalMono)
            {
                fontFamilies.Remove(mono);
                prioritized.Add(mono);
            }

            // Remaining installed fonts sorted alphabetically
            others.AddRange(fontFamilies.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

            var result = new List<string>(prioritized.Count + others.Count);
            result.AddRange(prioritized);
            result.AddRange(others);

            _cachedFonts = result;
            return _cachedFonts;
        }
    }

    private static void ReadFontsFromKey(RegistryKey root, string subKeyPath, HashSet<string> fontFamilies)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                var cleanName = CleanFontName(valueName);
                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    fontFamilies.Add(cleanName);
                }
            }
        }
        catch
        {
            // Registry read failures should not prevent app operation
        }
    }

    private static string CleanFontName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Strip font family combos like "Cambria & Cambria Math"
        if (name.Contains('&'))
        {
            name = name.Split('&')[0].Trim();
        }

        // Strip (TrueType), (OpenType), etc.
        name = TypeSuffixRegex.Replace(name, string.Empty).Trim();

        // Strip style suffixes (Regular, Bold, etc.)
        name = StyleSuffixRegex.Replace(name, string.Empty).Trim();

        return name;
    }
}
