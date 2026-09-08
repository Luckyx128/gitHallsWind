using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraQueryPresetsTests
{
    [Fact]
    public void TheBoardOpensOnTheActiveSprint()
    {
        Assert.Equal(JiraQueryPresets.ActiveSprintId, JiraQueryPresets.Default.Id);
        Assert.Contains("openSprints()", JiraQueryPresets.Default.Jql);
        Assert.True(JiraQueryPresets.Default.IsBuiltIn);
    }

    [Fact]
    public void PresetIdsAreDistinctAndMarkedBuiltIn()
    {
        Assert.Equal(JiraQueryPresets.All.Count, JiraQueryPresets.All.Select(q => q.Id).Distinct().Count());
        Assert.All(JiraQueryPresets.All, q => Assert.True(q.IsBuiltIn));
        Assert.All(JiraQueryPresets.All, q => Assert.False(string.IsNullOrWhiteSpace(q.Jql)));
    }

    [Fact]
    public void Combine_PutsPresetsFirstAndDropsACustomQueryThatShadowsOne()
    {
        var mine = new JiraQuery("mine", "Mine", "project = APP");
        var shadow = new JiraQuery(JiraQueryPresets.ActiveSprintId, "Shadow", "x");

        var combined = JiraQueryPresets.Combine(new[] { shadow, mine });

        Assert.Equal(JiraQueryPresets.All.Count + 1, combined.Count);
        Assert.Same(mine, combined[^1]);
        Assert.Equal("Active sprint", combined[0].Name);
    }

    [Fact]
    public void ASavedQueryDoesNotSerializeTheBuiltInFlag()
    {
        // The flag is code, not settings: a preset written to disk would come back
        // as a custom query the user could delete.
        var json = System.Text.Json.JsonSerializer.Serialize(new JiraQuery("id", "n", "jql", isBuiltIn: true));

        Assert.DoesNotContain("IsBuiltIn", json);
        Assert.DoesNotContain("isBuiltIn", json);
    }
}
