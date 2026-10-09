using System;
using System.Threading;
using System.Threading.Tasks;
using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraServiceTests
{
    [Fact]
    public void Constructor_CanCreate()
    {
        var service = new JiraService();
        Assert.NotNull(service);
    }
}
