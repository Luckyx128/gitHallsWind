using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GitHalls.Core.Clockify.Models;

namespace GitHalls.Core.Clockify;

public interface IClockifyClient
{
    Task<User> GetCurrentUserAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Workspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tag>> GetTagsAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(string workspaceId, string userId, CancellationToken cancellationToken = default);
    Task<TimeEntry> StartTimeEntryAsync(string workspaceId, TimeEntryRequest request, CancellationToken cancellationToken = default);
    Task<TimeEntry> StopTimeEntryAsync(string workspaceId, string userId, StopTimeEntryRequest request, CancellationToken cancellationToken = default);
}

public class ClockifyClient : IClockifyClient
{
    private readonly HttpClient _httpClient;
    private readonly IClockifyCredentialManager _credentialManager;

    public ClockifyClient(IClockifyCredentialManager credentialManager, HttpClient? httpClient = null)
    {
        _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://api.clockify.me/api/v1/");
        }
    }

    private void EnsureAuthorization()
    {
        var apiKey = _credentialManager.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Clockify API key is not set.");
        }
        
        _httpClient.DefaultRequestHeaders.Remove("X-Api-Key");
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<User> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.GetAsync("user", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<User>(cancellationToken: cancellationToken))!;
    }

    public async Task<IReadOnlyList<Workspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.GetAsync("workspaces", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<Workspace>>(cancellationToken: cancellationToken)) ?? new List<Workspace>();
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.GetAsync($"workspaces/{workspaceId}/projects", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<Project>>(cancellationToken: cancellationToken)) ?? new List<Project>();
    }

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.GetAsync($"workspaces/{workspaceId}/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<Tag>>(cancellationToken: cancellationToken)) ?? new List<Tag>();
    }

    public async Task<IReadOnlyList<TimeEntry>> GetTimeEntriesAsync(string workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.GetAsync($"workspaces/{workspaceId}/user/{userId}/time-entries", cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<TimeEntry>>(cancellationToken: cancellationToken)) ?? new List<TimeEntry>();
    }

    public async Task<TimeEntry> StartTimeEntryAsync(string workspaceId, TimeEntryRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.PostAsJsonAsync($"workspaces/{workspaceId}/time-entries", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TimeEntry>(cancellationToken: cancellationToken))!;
    }

    public async Task<TimeEntry> StopTimeEntryAsync(string workspaceId, string userId, StopTimeEntryRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorization();
        var response = await _httpClient.PatchAsJsonAsync($"workspaces/{workspaceId}/user/{userId}/time-entries", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TimeEntry>(cancellationToken: cancellationToken))!;
    }
}
