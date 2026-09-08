using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraBranchTypeTests
{
    [Theory]
    [InlineData("Bug", "fix")]
    [InlineData("Defect", "fix")]
    [InlineData("Story", "feature")]
    [InlineData("Epic", "feature")]
    [InlineData("New Feature", "feature")]
    [InlineData("Improvement", "feature")]
    [InlineData("Task", "chore")]
    [InlineData("Sub-task", "chore")]
    public void For_MapsTheEnglishTypesJiraShipsWith(string type, string expected)
    {
        Assert.Equal(expected, JiraBranchType.For(type));
    }

    [Theory]
    [InlineData("Erro", "fix")]
    [InlineData("Defeito", "fix")]
    [InlineData("História", "feature")]
    [InlineData("Épico", "feature")]
    [InlineData("Melhoria", "feature")]
    [InlineData("Nova Funcionalidade", "feature")]
    [InlineData("Tarefa", "chore")]
    [InlineData("Subtarefa", "chore")]
    public void For_MapsThePortugueseNamesTheProjectActuallyUses(string type, string expected)
    {
        Assert.Equal(expected, JiraBranchType.For(type));
    }

    [Theory]
    [InlineData("BUG")]
    [InlineData("bug")]
    [InlineData("  Bug  ")]
    public void For_IgnoresCaseAndSurroundingSpace(string type)
    {
        Assert.Equal("fix", JiraBranchType.For(type));
    }

    [Theory]
    [InlineData("HISTÓRIA", "feature")]
    [InlineData("historia", "feature")]
    [InlineData("Manutenção", "chore")]
    public void For_IgnoresAccents(string type, string expected)
    {
        Assert.Equal(expected, JiraBranchType.For(type));
    }

    [Theory]
    [InlineData("Bug de Produção", "fix")]
    [InlineData("Story - Frontend", "feature")]
    [InlineData("Tarefa técnica", "chore")]
    public void For_ReadsACompoundTypeByItsWords(string type, string expected)
    {
        Assert.Equal(expected, JiraBranchType.For(type));
    }

    [Fact]
    public void For_ReadsWordsAndNotSubstrings()
    {
        // "Debug tooling" contains "bug" and is not a bug.
        Assert.Equal("feature", JiraBranchType.For("Debug tooling"));
    }

    [Theory]
    [InlineData("Spike")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void For_FallsBackToFeatureForATypeItDoesNotKnow(string? type)
    {
        Assert.Equal("feature", JiraBranchType.For(type));
    }
}
