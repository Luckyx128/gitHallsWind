using GitHalls.Core.Diff;
using Xunit;

namespace GitHalls.Core.Tests.Diff;

public class SyntaxLanguageTests
{
    [Theory]
    [InlineData("src/Program.cs", "csharp")]
    [InlineData("GitHalls.App/MainWindow.xaml", "xml")]
    [InlineData("GitHalls.Core/GitHalls.Core.csproj", "xml")]
    [InlineData("scripts/build.ps1", "powershell")]
    [InlineData("GitHalls/GIt/GitService.swift", "swift")]
    [InlineData("app/main.ts", "typescript")]
    [InlineData("README.md", "markdown")]
    public void ForPath_MapsKnownExtensions(string path, string expected)
    {
        Assert.Equal(expected, SyntaxLanguage.ForPath(path));
    }

    [Theory]
    [InlineData("Dockerfile", "dockerfile")]
    [InlineData("build/Makefile", "makefile")]
    [InlineData("CMakeLists.txt", "cmake")]
    public void ForPath_MapsKnownBasenames(string path, string expected)
    {
        Assert.Equal(expected, SyntaxLanguage.ForPath(path));
    }

    [Fact]
    public void ForPath_HandlesWindowsSeparators()
    {
        Assert.Equal("csharp", SyntaxLanguage.ForPath(@"GitHalls.Core\Git\GitService.cs"));
    }

    [Theory]
    [InlineData("assets/logo.png")]
    [InlineData("LICENSE")]
    [InlineData("")]
    [InlineData(null)]
    public void ForPath_UnknownReturnsNull(string? path)
    {
        Assert.Null(SyntaxLanguage.ForPath(path));
    }
}
