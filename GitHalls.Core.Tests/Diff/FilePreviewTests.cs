using GitHalls.Core.Diff;
using Xunit;

namespace GitHalls.Core.Tests.Diff;

public class FilePreviewTests
{
    [Theory]
    [InlineData("assets/logo.png")]
    [InlineData("photo.JPG")]
    [InlineData("a/b/c.jpeg")]
    [InlineData("icon.gif")]
    [InlineData("shot.webp")]
    [InlineData("old.bmp")]
    [InlineData("IMG_0042.HEIC")]
    [InlineData("scan.tiff")]
    [InlineData("favicon.ico")]
    [InlineData("assets\\windows\\logo.png")]
    public void KindFor_ReadsAnImageByItsExtension(string path)
    {
        Assert.Equal(FilePreviewKind.Image, FilePreview.KindFor(path));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("archive.zip")]
    [InlineData("Font.ttf")]
    [InlineData("notes.docx")]
    [InlineData("Makefile")]
    [InlineData("bin/tool")]
    [InlineData("data.sqlite")]
    [InlineData("")]
    [InlineData(null)]
    public void KindFor_TreatsEverythingElseAsSomethingToDescribe(string? path)
    {
        Assert.Equal(FilePreviewKind.Other, FilePreview.KindFor(path));
    }

    [Theory]
    [InlineData("png/notes.txt")]
    [InlineData("my.png.bak")]
    public void KindFor_DoesNotMistakeANameForAnExtension(string path)
    {
        Assert.Equal(FilePreviewKind.Other, FilePreview.KindFor(path));
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(512, "512 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1572864, "1.5 MB")]
    public void FormattedSize_ReadsAsASizeAndNotAByteCount(long bytes, string expected)
    {
        Assert.Equal(expected, FilePreview.FormattedSize(bytes));
    }
}
