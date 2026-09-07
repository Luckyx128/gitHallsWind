using GitHalls.Core.Jira;

namespace GitHalls.App.Services;

/// <summary>
/// The connected Jira account, assembled from two places on purpose: the site
/// and email sit in settings.json, where they are readable and editable, and the
/// API token sits in the Windows Credential Manager, where it belongs.
/// </summary>
public sealed class JiraAccountStore
{
    /// <summary>What the entry is called in the Credential Manager.</summary>
    private const string CredentialTarget = "GitHalls:jira";

    private readonly SettingsStore _settings;

    public JiraAccountStore(SettingsStore settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => Current != null;

    /// <summary>Everything needed to call Jira, or null while any part is missing.</summary>
    public JiraCredentials? Current
    {
        get
        {
            var site = JiraCredentials.NormalizeSite(_settings.Current.JiraSite);
            var email = _settings.Current.JiraEmail;
            if (site == null || string.IsNullOrWhiteSpace(email)) return null;

            var token = ReadToken();
            return string.IsNullOrEmpty(token) ? null : new JiraCredentials(site, email, token);
        }
    }

    public string SiteText => _settings.Current.JiraSite ?? string.Empty;
    public string Email => _settings.Current.JiraEmail;

    public async Task SaveAsync(Uri site, string email, string token)
    {
        // The token first: if the vault refuses, the settings must not end up
        // pointing at an account with no way to authenticate.
        CredentialStore.Save(CredentialTarget, email, token);

        await _settings.UpdateAsync(settings =>
        {
            settings.JiraSite = site.AbsoluteUri;
            settings.JiraEmail = email;
        });
    }

    public async Task ForgetAsync()
    {
        CredentialStore.Delete(CredentialTarget);

        await _settings.UpdateAsync(settings =>
        {
            settings.JiraSite = null;
            settings.JiraEmail = string.Empty;
        });
    }

    private static string? ReadToken()
    {
        try
        {
            return CredentialStore.Read(CredentialTarget);
        }
        catch
        {
            // A vault that won't answer reads as "not connected", which is what
            // the UI can actually act on.
            return null;
        }
    }
}
