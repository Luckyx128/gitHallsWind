using System;
using System.Text.Json.Serialization;

namespace GitHalls.Core.Clockify.Models;

public class Workspace
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class Project
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string WorkspaceId { get; set; } = string.Empty;
    [JsonPropertyName("color")] public string Color { get; set; } = string.Empty;
}

public class Tag
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string WorkspaceId { get; set; } = string.Empty;
}

public class TimeEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("projectId")] public string? ProjectId { get; set; }
    [JsonPropertyName("workspaceId")] public string WorkspaceId { get; set; } = string.Empty;
    [JsonPropertyName("timeInterval")] public TimeInterval TimeInterval { get; set; } = new();
}

public class TimeInterval
{
    [JsonPropertyName("start")] public DateTimeOffset Start { get; set; }
    [JsonPropertyName("end")] public DateTimeOffset? End { get; set; }
}

public class User
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("activeWorkspace")] public string ActiveWorkspace { get; set; } = string.Empty;
}

public class TimeEntryRequest
{
    [JsonPropertyName("start")] public DateTimeOffset Start { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("projectId")] public string? ProjectId { get; set; }
    [JsonPropertyName("tagIds")] public string[]? TagIds { get; set; }
}

public class StopTimeEntryRequest
{
    [JsonPropertyName("end")] public DateTimeOffset End { get; set; }
}
