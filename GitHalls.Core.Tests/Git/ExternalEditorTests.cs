using GitHalls.Core.Git;
using Xunit;

namespace GitHalls.Core.Tests.Git;

public class ExternalEditorTests
{
    [Fact]
    public void Known_ListsEachEditorOnce()
    {
        var commands = ExternalEditors.Known.Select(e => e.Command).ToList();

        Assert.Equal(commands.Count, commands.Distinct().Count());
    }

    [Fact]
    public void Known_NamesEveryEditorItOffers()
    {
        Assert.All(ExternalEditors.Known, editor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(editor.Name));
            Assert.False(string.IsNullOrWhiteSpace(editor.Command));
        });
    }

    [Fact]
    public void Known_UsesEnvironmentVariablesRatherThanHardCodedDrives()
    {
        // A path starting at C:\ is wrong on any machine that installed
        // elsewhere; every candidate has to go through a variable.
        var literal = ExternalEditors.Known
            .SelectMany(e => e.InstallPaths)
            .Where(p => !p.StartsWith('%'))
            .ToList();

        Assert.Empty(literal);
    }

    [Fact]
    public void Locate_ReturnsNullForAnEditorThatIsNowhere()
    {
        var nowhere = new ExternalEditor("Nowhere", "githalls-no-such-editor", new[] { @"%LOCALAPPDATA%\NoSuchEditor\nope.exe" });

        Assert.Null(ExternalEditors.Locate(nowhere));
    }

    [Fact]
    public void Installed_IsASubsetOfWhatIsKnown()
    {
        Assert.All(ExternalEditors.Installed(), editor => Assert.Contains(editor, ExternalEditors.Known));
    }
}
