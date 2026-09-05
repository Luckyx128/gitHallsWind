using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;
using GitHalls.App.Themes;

namespace GitHalls.App.Services;

/// <summary>A run of text sharing one token colour.</summary>
public readonly record struct StyledRun(string Text, DiffTokenKind Kind);

/// <summary>Turns source text into per-line styled runs.</summary>
public interface IDiffHighlighter
{
    /// <summary>
    /// Highlights <paramref name="text"/> and returns one entry per line —
    /// exactly <c>count('\n') + 1</c> entries. Never throws: any failure falls
    /// back to a single plain run per line.
    /// </summary>
    IReadOnlyList<IReadOnlyList<StyledRun>> HighlightLines(string text, string? languageId);
}

/// <summary>Everything is plain text. The floor every other implementation falls back to.</summary>
public sealed class PlainDiffHighlighter : IDiffHighlighter
{
    public static readonly PlainDiffHighlighter Instance = new();

    public IReadOnlyList<IReadOnlyList<StyledRun>> HighlightLines(string text, string? languageId) => Split(text);

    internal static IReadOnlyList<IReadOnlyList<StyledRun>> Split(string text)
        => text.Split('\n')
               .Select(line => (IReadOnlyList<StyledRun>)new[] { new StyledRun(line, DiffTokenKind.Plain) })
               .ToList();
}

/// <summary>
/// ColorCode-backed highlighter. Port of DiffSyntaxHighlighter.swift.
/// </summary>
public sealed class ColorCodeDiffHighlighter : CodeColorizerBase, IDiffHighlighter
{
    public static readonly ColorCodeDiffHighlighter Instance = new();

    /// <summary>Above these sizes highlighting is skipped, so a huge generated-file diff still opens instantly.</summary>
    private const int MaxCharacters = 150_000;
    private const int MaxLines = 6_000;

    /// <summary>The parser writes into <see cref="_runs"/> through a callback, so one text at a time.</summary>
    private readonly object _gate = new();
    private readonly List<StyledRun> _runs = new();

    public ColorCodeDiffHighlighter() : base(null, null) { }

    public IReadOnlyList<IReadOnlyList<StyledRun>> HighlightLines(string text, string? languageId)
    {
        var lineCount = text.Count(c => c == '\n') + 1;

        if (string.IsNullOrEmpty(languageId) || text.Length > MaxCharacters || lineCount > MaxLines)
        {
            return PlainDiffHighlighter.Split(text);
        }

        var language = FindLanguage(languageId);
        if (language == null) return PlainDiffHighlighter.Split(text);

        List<StyledRun> flat;
        try
        {
            lock (_gate)
            {
                _runs.Clear();
                languageParser.Parse(text, language, (parsedSourceCode, captures) => Write(parsedSourceCode, captures));
                flat = new List<StyledRun>(_runs);
                _runs.Clear();
            }
        }
        catch
        {
            return PlainDiffHighlighter.Split(text);
        }

        var lines = SplitRunsIntoLines(flat);

        // The parser can, in rare cases, not round-trip the text 1:1. Bail to
        // plain rather than misalign every line against its number.
        return lines.Count == lineCount ? lines : PlainDiffHighlighter.Split(text);
    }

    protected override void Write(string parsedSourceCode, IList<Scope> scopes)
    {
        var insertions = new List<TextInsertion>();
        foreach (var scope in scopes)
        {
            GetStyleInsertionsForCapturedStyle(scope, insertions);
        }

        // OrderBy is a stable sort, which matters here: nested scopes sharing an
        // index have to keep the order the parser produced them in.
        var ordered = insertions.OrderBy(i => i.Index).ToList();

        var offset = 0;
        Scope? current = null;

        foreach (var insertion in ordered)
        {
            if (insertion.Index > offset)
            {
                Append(parsedSourceCode.Substring(offset, insertion.Index - offset), current);
            }

            // An insertion with no scope closes the one that was open.
            current = insertion.Scope;
            offset = insertion.Index;
        }

        if (offset < parsedSourceCode.Length)
        {
            Append(parsedSourceCode.Substring(offset), current);
        }
    }

    private void GetStyleInsertionsForCapturedStyle(Scope scope, ICollection<TextInsertion> styleInsertions)
    {
        styleInsertions.Add(new TextInsertion
        {
            Index = scope.Index,
            Scope = scope
        });

        foreach (Scope childScope in scope.Children)
        {
            GetStyleInsertionsForCapturedStyle(childScope, styleInsertions);
        }

        styleInsertions.Add(new TextInsertion
        {
            Index = scope.Index + scope.Length
        });
    }

    private void Append(string text, Scope? scope)
    {
        if (text.Length == 0) return;
        _runs.Add(new StyledRun(text, MapScope(scope?.Name)));
    }

    /// <summary>Splits a flat run list on newlines, keeping each run's token kind.</summary>
    private static List<IReadOnlyList<StyledRun>> SplitRunsIntoLines(List<StyledRun> flat)
    {
        var lines = new List<IReadOnlyList<StyledRun>>();
        var current = new List<StyledRun>();

        foreach (var run in flat)
        {
            var pieces = run.Text.Split('\n');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0)
                {
                    lines.Add(current);
                    current = new List<StyledRun>();
                }
                if (pieces[i].Length > 0) current.Add(new StyledRun(pieces[i], run.Kind));
            }
        }

        lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Ids from Core's SyntaxLanguage are highlight.js-style names; ColorCode
    /// uses its own ("c#", "vb.net"). Each entry is tried in order and an
    /// unknown id simply yields plain text, so adding a language to Core can
    /// never break rendering here.
    /// </summary>
    private static readonly Dictionary<string, string[]> LanguageIdCandidates = new()
    {
        ["csharp"] = new[] { "c#", "csharp" },
        ["cpp"] = new[] { "cpp", "c++" },
        ["c"] = new[] { "cpp", "c++" },
        ["objectivec"] = new[] { "cpp", "c++" },
        ["fsharp"] = new[] { "f#", "fsharp" },
        ["vbnet"] = new[] { "vb.net", "vbnet" },
        ["javascript"] = new[] { "javascript", "js" },
        ["typescript"] = new[] { "typescript", "ts" },
        ["xml"] = new[] { "xml", "html" },
        ["markdown"] = new[] { "markdown", "md" },
        ["powershell"] = new[] { "powershell", "posh" },
        ["dos"] = new[] { "powershell" },
    };

    private static ILanguage? FindLanguage(string languageId)
    {
        var candidates = LanguageIdCandidates.TryGetValue(languageId, out var mapped)
            ? mapped
            : new[] { languageId };

        foreach (var candidate in candidates)
        {
            try
            {
                var language = Languages.FindById(candidate);
                if (language != null) return language;
            }
            catch
            {
                // FindById is a lookup; treat any surprise as "no grammar".
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a ColorCode scope name onto a colour category.
    ///
    /// Matched loosely, on purpose. Scope names are the library's data ("Preprocessor
    /// Keyword", "HTML Attribute Value", "String (C# @ Verbatim)"), they differ per
    /// grammar, and new ones appear with new grammars. A substring match means an
    /// unrecognized scope degrades to a sensible colour instead of rendering as
    /// undifferentiated plain text.
    /// </summary>
    private static DiffTokenKind MapScope(string? name)
    {
        if (string.IsNullOrEmpty(name)) return DiffTokenKind.Plain;

        // Order matters: "Preprocessor Keyword" also contains "Keyword", and
        // "HTML Attribute Value" is a string, not an attribute name.
        if (Has(name, "Comment")) return DiffTokenKind.Comment;
        if (Has(name, "Preprocessor")) return DiffTokenKind.Preprocessor;
        if (Has(name, "Attribute Value")) return DiffTokenKind.String;
        if (Has(name, "String")) return DiffTokenKind.String;
        if (Has(name, "Keyword")) return DiffTokenKind.Keyword;
        if (Has(name, "Number")) return DiffTokenKind.Number;
        if (Has(name, "Attribute")) return DiffTokenKind.Attribute;
        if (Has(name, "Type") || Has(name, "Class") || Has(name, "Element Name") || Has(name, "Name Space")) return DiffTokenKind.Type;
        if (Has(name, "Delimiter") || Has(name, "Operator")) return DiffTokenKind.Operator;

        return DiffTokenKind.Plain;
    }

    private static bool Has(string name, string token) => name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
