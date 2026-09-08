namespace GitHalls.Core.Diff;

/// <summary>What a file with no text diff can be shown as.</summary>
public enum FilePreviewKind
{
    /// <summary>Something the app can draw. Shown before beside after.</summary>
    Image,

    /// <summary>Anything else: a size, a type, and a way to open it where it belongs.</summary>
    Other
}

/// <summary>
/// The two sides of a binary file's change. Either may be null: a file that was
/// just added has no before, and a deleted one has no after.
/// </summary>
public sealed record BinaryFileContents(string FilePath, byte[]? Before, byte[]? After)
{
    public FilePreviewKind Kind => FilePreview.KindFor(FilePath);

    public bool HasSomethingToShow => Before != null || After != null;
}

/// <summary>
/// git only tells us a file is binary, never what kind — so the decision comes
/// from the path, which is the one thing available before any bytes are read.
/// </summary>
public static class FilePreview
{
    /// <summary>
    /// Formats a WinUI BitmapImage decodes. SVG is absent on purpose: git diffs
    /// it as text, so it never reaches here.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff", ".ico"
    };

    public static FilePreviewKind KindFor(string? path)
    {
        var extension = Path.GetExtension(path?.Replace('\\', '/') ?? string.Empty);

        return ImageExtensions.Contains(extension) ? FilePreviewKind.Image : FilePreviewKind.Other;
    }

    /// <summary>"1.2 MB" — what a binary change has instead of a line count.</summary>
    public static string FormattedSize(long byteCount)
    {
        string[] units = { "bytes", "KB", "MB", "GB" };

        double size = byteCount;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        // Bytes are whole things; anything larger reads better rounded.
        return unit == 0 ? $"{byteCount} {units[0]}" : $"{size:0.#} {units[unit]}";
    }
}
