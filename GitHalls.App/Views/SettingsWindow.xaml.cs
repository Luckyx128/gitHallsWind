using GitHalls.App.Helpers;
using GitHalls.App.Services;
using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace GitHalls.App.Views;

/// <summary>One saved identity, as this window lists it.</summary>
public sealed class IdentityListRow
{
    public GitIdentity Identity { get; init; } = new();

    public string Title => Identity.DisplayName;
    public string Subtitle => string.IsNullOrWhiteSpace(Identity.GitHubUsername)
        ? Identity.Email
        : $"{Identity.Email} · {Identity.GitHubUsername}";

    public string ToolTip => $"{Identity.Name} <{Identity.Email}>";
}

/// <summary>
/// Account settings: the Jira connection and the git identities.
///
/// A window of its own rather than a dialog — it is the one screen the user
/// comes back to, and both halves are forms with several fields and their own
/// failure states.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly JiraAccountStore _account;
    private readonly RepositoryViewModel _repository;

    private readonly ObservableCollection<IdentityListRow> _identities = new();

    /// <summary>The identity being edited, or null while the form is a new one.</summary>
    private GitIdentity? _editing;

    /// <summary>Raised when the Jira account is connected or disconnected.</summary>
    public event EventHandler? AccountChanged;

    public SettingsWindow(JiraAccountStore account, RepositoryViewModel repository)
    {
        _account = account;
        _repository = repository;

        InitializeComponent();
        this.ConfigureCustomTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 900));

        IdentityList.ItemsSource = _identities;

        SiteTextBox.Text = _account.SiteText;
        EmailTextBox.Text = _account.Email;

        ReloadIdentities();
        UpdateJiraState();
        NewIdentity_Click(this, new RoutedEventArgs());
    }

    // MARK: - Jira

    private void UpdateJiraState()
    {
        var connected = _account.IsConfigured;

        JiraStatusText.Text = connected
            ? $"Connected as {_account.Email}."
            : "Not connected.";

        DisconnectButton.IsEnabled = connected;
        TokenPasswordBox.PlaceholderText = connected ? "•••••••• (unchanged)" : string.Empty;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var site = JiraCredentials.NormalizeSite(SiteTextBox.Text);
        if (site == null)
        {
            ShowJira("That site address doesn't look like a URL.", InfoBarSeverity.Error);
            return;
        }

        var email = EmailTextBox.Text.Trim();
        if (email.Length == 0)
        {
            ShowJira("The account email is required.", InfoBarSeverity.Error);
            return;
        }

        // An empty box means "keep the token you already have", so changing the
        // site or the email doesn't force the user to paste the token again.
        var token = TokenPasswordBox.Password.Length > 0
            ? TokenPasswordBox.Password
            : _account.Current?.Token;

        if (string.IsNullOrEmpty(token))
        {
            ShowJira("An API token is required.", InfoBarSeverity.Error);
            return;
        }

        SetConnecting(true);
        try
        {
            // Verified before it is saved: settings that point at an account
            // Jira rejects are worse than no settings at all.
            var account = await new JiraClient(new JiraCredentials(site, email, token)).MyselfAsync();
            await _account.SaveAsync(site, email, token);

            TokenPasswordBox.Password = string.Empty;
            SiteTextBox.Text = site.AbsoluteUri;

            ShowJira($"Connected as {account.DisplayName}.", InfoBarSeverity.Success);
            UpdateJiraState();
            AccountChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowJira(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetConnecting(false);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await _account.ForgetAsync();

        TokenPasswordBox.Password = string.Empty;
        ShowJira("Disconnected. The token was removed from the Credential Manager.", InfoBarSeverity.Informational);
        UpdateJiraState();
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetConnecting(bool connecting)
    {
        ConnectButton.IsEnabled = !connecting;
        ConnectRing.IsActive = connecting;
        ConnectRing.Visibility = connecting ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowJira(string message, InfoBarSeverity severity)
    {
        JiraInfoBar.Message = message;
        JiraInfoBar.Severity = severity;
        JiraInfoBar.IsOpen = true;
    }

    // MARK: - Identities

    private void ReloadIdentities()
    {
        _identities.Clear();
        foreach (var identity in _repository.SavedIdentities)
        {
            _identities.Add(new IdentityListRow { Identity = identity });
        }

        NoIdentitiesText.Visibility = _identities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IdentityList.Visibility = _identities.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void IdentityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IdentityList.SelectedItem is not IdentityListRow row)
        {
            return;
        }

        _editing = row.Identity;
        LabelTextBox.Text = row.Identity.Label;
        NameTextBox.Text = row.Identity.Name;
        IdentityEmailTextBox.Text = row.Identity.Email;
        GitHubUserTextBox.Text = row.Identity.GitHubUsername;
        DeleteIdentityButton.IsEnabled = true;
    }

    private void NewIdentity_Click(object sender, RoutedEventArgs e)
    {
        _editing = null;
        IdentityList.SelectedItem = null;

        LabelTextBox.Text = string.Empty;
        NameTextBox.Text = string.Empty;
        IdentityEmailTextBox.Text = string.Empty;
        GitHubUserTextBox.Text = string.Empty;
        GitHubTokenPasswordBox.Password = string.Empty;
        DeleteIdentityButton.IsEnabled = false;
    }

    private void SaveIdentity_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var email = IdentityEmailTextBox.Text.Trim();

        if (name.Length == 0 || email.Length == 0)
        {
            ShowIdentity("A name and an email are what git records; both are required.", InfoBarSeverity.Error);
            return;
        }

        // Editing keeps the id, so the profile is replaced rather than duplicated.
        var identity = _editing ?? new GitIdentity();
        identity.Label = LabelTextBox.Text.Trim();
        identity.Name = name;
        identity.Email = email;
        identity.GitHubUsername = GitHubUserTextBox.Text.Trim();

        _repository.SaveIdentity(identity);
        _editing = identity;

        ReloadIdentities();
        ShowIdentity($"Saved {identity.DisplayName}.", InfoBarSeverity.Success);
    }

    private void DeleteIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;

        _repository.RemoveIdentity(_editing.Id);
        ReloadIdentities();
        NewIdentity_Click(sender, e);
        ShowIdentity("Identity removed. Nothing changed in any repository.", InfoBarSeverity.Informational);
    }

    private async void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        var username = GitHubUserTextBox.Text.Trim();
        var token = GitHubTokenPasswordBox.Password;

        if (username.Length == 0 || token.Length == 0)
        {
            ShowIdentity("A GitHub username and a token are both needed.", InfoBarSeverity.Error);
            return;
        }

        SaveTokenButton.IsEnabled = false;
        try
        {
            var saved = await _repository.SaveGitHubTokenAsync(token, username);

            GitHubTokenPasswordBox.Password = string.Empty;
            ShowIdentity(
                saved ? $"Token handed to git's credential helper for {username}." : _repository.ErrorMessage ?? "Could not store the token.",
                saved ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        finally
        {
            SaveTokenButton.IsEnabled = true;
        }
    }

    private void ShowIdentity(string message, InfoBarSeverity severity)
    {
        IdentityInfoBar.Message = message;
        IdentityInfoBar.Severity = severity;
        IdentityInfoBar.IsOpen = true;
    }
}
