using System.Text.RegularExpressions;

namespace GitHalls.Core.GitHub;

public record PullRequestRule(string DestinationBranchPattern, string JiraStatus);

public class PullRequestRuleEngine
{
    private readonly IReadOnlyList<PullRequestRule> _rules;

    public PullRequestRuleEngine(IReadOnlyList<PullRequestRule> rules)
    {
        _rules = rules ?? Array.Empty<PullRequestRule>();
    }

    public string? EvaluateJiraStatus(string destinationBranchName)
    {
        if (string.IsNullOrWhiteSpace(destinationBranchName))
            return null;

        foreach (var rule in _rules)
        {
            try
            {
                if (Regex.IsMatch(destinationBranchName, rule.DestinationBranchPattern))
                {
                    return rule.JiraStatus;
                }
            }
            catch (RegexParseException)
            {
                // Ignore invalid regex in user-defined rules and continue
            }
        }

        return null;
    }
}
