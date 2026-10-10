using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHalls.App.Services;
using GitHalls.Core.Clockify;
using GitHalls.Core.Clockify.Models;
using GitHalls.Core.Jira;
using Microsoft.UI.Dispatching;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GitHalls.App.ViewModels;

public partial class ClockifyViewModel : ObservableObject
{
    private readonly IClockifyClient _client;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _timer;

    [ObservableProperty]
    private TimeEntry? _runningTimer;

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private bool _isTimerRunning;

    [ObservableProperty]
    private string? _errorMessage;

    private User? _currentUser;
    private Workspace? _activeWorkspace;
    private Project? _activeProject; // Ideally matched to Jira project

    public ClockifyViewModel(SettingsStore settings)
    {
        _settings = settings;
        _client = new ClockifyClient(new ClockifyCredentialManager());
        _dispatcher = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += Timer_Tick;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var apiKey = new ClockifyCredentialManager().GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) return;

            _currentUser = await _client.GetCurrentUserAsync();
            var workspaces = await _client.GetWorkspacesAsync();
            _activeWorkspace = workspaces.FirstOrDefault();

            if (_activeWorkspace != null && _currentUser != null)
            {
                var entries = await _client.GetTimeEntriesAsync(_activeWorkspace.Id, _currentUser.Id);
                RunningTimer = entries.FirstOrDefault(e => e.TimeInterval.End == null);
                IsTimerRunning = RunningTimer != null;
                if (IsTimerRunning)
                {
                    UpdateElapsed();
                    _timer?.Start();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void Timer_Tick(object? sender, object e)
    {
        UpdateElapsed();
    }

    private void UpdateElapsed()
    {
        if (RunningTimer != null)
        {
            Elapsed = DateTimeOffset.UtcNow - RunningTimer.TimeInterval.Start;
        }
    }

    public async Task StartTimerAsync(JiraIssue issue)
    {
        if (_activeWorkspace == null) return;
        try
        {
            var request = new TimeEntryRequest
            {
                Start = DateTimeOffset.UtcNow,
                Description = $"{issue.Key} {issue.Summary}"
            };
            RunningTimer = await _client.StartTimeEntryAsync(_activeWorkspace.Id, request);
            IsTimerRunning = true;
            UpdateElapsed();
            _timer?.Start();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task StopTimerAsync()
    {
        if (_activeWorkspace == null || _currentUser == null) return;
        try
        {
            var request = new StopTimeEntryRequest { End = DateTimeOffset.UtcNow };
            await _client.StopTimeEntryAsync(_activeWorkspace.Id, _currentUser.Id, request);
            RunningTimer = null;
            IsTimerRunning = false;
            _timer?.Stop();
            Elapsed = TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task AddManualTimeEntryAsync(JiraIssue issue, DateTimeOffset date, TimeSpan start, TimeSpan end)
    {
        if (_activeWorkspace == null || _currentUser == null) return;
        try
        {
            var startDate = new DateTimeOffset(date.Year, date.Month, date.Day, start.Hours, start.Minutes, start.Seconds, TimeSpan.Zero);
            var endDate = new DateTimeOffset(date.Year, date.Month, date.Day, end.Hours, end.Minutes, end.Seconds, TimeSpan.Zero);
            
            var request = new TimeEntryRequest
            {
                Start = startDate,
                Description = $"{issue.Key} {issue.Summary}"
            };
            await _client.StartTimeEntryAsync(_activeWorkspace.Id, request);

            var stopRequest = new StopTimeEntryRequest { End = endDate };
            await _client.StopTimeEntryAsync(_activeWorkspace.Id, _currentUser.Id, stopRequest);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

