using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitHalls.Core.Clockify;
using GitHalls.Core.Clockify.Models;
using Xunit;

namespace GitHalls.Core.Tests.Clockify;

public class ClockifyClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class MockCredentialManager : IClockifyCredentialManager
    {
        public string? ApiKey { get; set; } = "test-api-key";

        public void ClearApiKey() => ApiKey = null;
        public string? GetApiKey() => ApiKey;
        public void SetApiKey(string apiKey) => ApiKey = apiKey;
    }

    [Fact]
    public async Task GetWorkspacesAsync_SendsCorrectRequestAndParsesResponse()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://api.clockify.me/api/v1/workspaces", req.RequestUri?.ToString());
            Assert.Contains("X-Api-Key", req.Headers.Select(h => h.Key));

            var workspaces = new List<Workspace>
            {
                new Workspace { Id = "ws1", Name = "Workspace 1" }
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(workspaces))
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new ClockifyClient(new MockCredentialManager(), httpClient);

        var result = await client.GetWorkspacesAsync();

        Assert.Single(result);
        Assert.Equal("ws1", result[0].Id);
        Assert.Equal("Workspace 1", result[0].Name);
    }

    [Fact]
    public async Task StartTimeEntryAsync_SendsCorrectRequestAndParsesResponse()
    {
        var requestModel = new TimeEntryRequest
        {
            Start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            Description = "Test task",
            ProjectId = "p1"
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api.clockify.me/api/v1/workspaces/ws1/time-entries", req.RequestUri?.ToString());
            
            var responseModel = new TimeEntry
            {
                Id = "te1",
                Description = "Test task",
                ProjectId = "p1",
                WorkspaceId = "ws1"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseModel))
            };
        });

        var httpClient = new HttpClient(handler);
        var client = new ClockifyClient(new MockCredentialManager(), httpClient);

        var result = await client.StartTimeEntryAsync("ws1", requestModel);

        Assert.Equal("te1", result.Id);
        Assert.Equal("Test task", result.Description);
        Assert.Equal("p1", result.ProjectId);
        Assert.Equal("ws1", result.WorkspaceId);
    }

    [Fact]
    public async Task EnsureAuthorization_ThrowsWhenApiKeyIsNotSet()
    {
        var credentialManager = new MockCredentialManager { ApiKey = null };
        var client = new ClockifyClient(credentialManager, new HttpClient(new MockHttpMessageHandler(_ => new HttpResponseMessage())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWorkspacesAsync());
    }
}
