using System;

namespace GitHalls.Core.Jira;

public sealed record JiraComment(
    string Id,
    JiraAccount? Author,
    string Body,
    DateTimeOffset Created,
    DateTimeOffset Updated);
