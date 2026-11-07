using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.Core.Services;

/// <summary>Owns "who is signed in": resuming saved accounts, completing sign-ins, switching users.</summary>
public sealed class SessionService
{
    private readonly JellyfinClient _client;
    private readonly AccountStore _accounts;
    private readonly SettingsStore _settings;

    public SavedAccount? Current { get; private set; }

    /// <summary>
    /// The signed-in user's playback preferences from their Jellyfin profile (languages, subtitle
    /// mode). Per user, so each person in a household gets their own defaults on a shared device.
    /// </summary>
    public UserConfiguration? UserConfig { get; private set; }

    /// <summary>Audio language to prefer: this device's setting if set, else the user's Jellyfin profile.</summary>
    public string? PreferredAudioLanguage(string? deviceSetting) =>
        !string.IsNullOrWhiteSpace(deviceSetting) ? deviceSetting : NullIfEmpty(UserConfig?.AudioLanguagePreference);

    /// <summary>Subtitle language to prefer: this device's setting if set, else the user's Jellyfin profile.</summary>
    public string? PreferredSubtitleLanguage(string? deviceSetting) =>
        !string.IsNullOrWhiteSpace(deviceSetting) ? deviceSetting : NullIfEmpty(UserConfig?.SubtitleLanguagePreference);

    /// <summary>The user chose "no subtitles" in Jellyfin (and this device doesn't ask for a subtitle language).</summary>
    public bool SubtitlesOff(string? deviceSubtitleSetting) =>
        string.IsNullOrWhiteSpace(deviceSubtitleSetting) && UserConfig?.SubtitleMode == "None";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    public event Action? SessionChanged;

    public SessionService(JellyfinClient client, AccountStore accounts, SettingsStore settings)
    {
        _client = client;
        _accounts = accounts;
        _settings = settings;
    }

    public IReadOnlyList<SavedAccount> SavedAccounts => _accounts.Accounts;

    /// <summary>Account to sign into automatically at startup, if the user enabled auto-login.</summary>
    public SavedAccount? AutoLoginCandidate =>
        _settings.Current.AutoLogin ? _accounts.Find(_settings.Current.LastAccountId) : null;

    /// <summary>Validates the stored token. Returns false if the server rejected it (needs re-login).</summary>
    public async Task<bool> ResumeAsync(SavedAccount account, CancellationToken ct = default)
    {
        _client.Configure(account.ServerUrl, account.DeviceId, account.AccessToken, account.UserId);
        try
        {
            var me = await _client.GetMeAsync(ct).ConfigureAwait(false);
            account.UserName = me.Name ?? account.UserName;
            account.AvatarTag = me.PrimaryImageTag;
            account.IsAdmin = me.Policy?.IsAdministrator ?? account.IsAdmin;
            UserConfig = me.Configuration;
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            _client.ClearSession();
            return false;
        }
        await ActivateAsync(account).ConfigureAwait(false);
        return true;
    }

    /// <summary>Prepare the client for a fresh login against <paramref name="serverUrl"/>.</summary>
    public string BeginLogin(string serverUrl)
    {
        var deviceId = Guid.NewGuid().ToString("N");
        _client.Configure(serverUrl, deviceId);
        return deviceId;
    }

    public async Task<SavedAccount> CompleteLoginAsync(string serverUrl, PublicSystemInfo server, AuthenticationResult auth, bool remember)
    {
        if (auth.User == null || string.IsNullOrEmpty(auth.AccessToken)) throw new ApiException("Sign-in failed: the server returned no session.");

        // Signing in again as an already-saved user replaces that saved entry.
        var existing = _accounts.FindByUser(server.Id ?? "", auth.User.Id);
        var account = existing ?? new SavedAccount();
        account.ServerUrl = serverUrl;
        account.ServerId = server.Id;
        account.ServerName = server.ServerName;
        account.UserId = auth.User.Id;
        account.UserName = auth.User.Name ?? "";
        account.AvatarTag = auth.User.PrimaryImageTag;
        account.AccessToken = auth.AccessToken;
        account.DeviceId = _client.DeviceId;
        account.IsAdmin = auth.User.Policy?.IsAdministrator ?? false;
        UserConfig = auth.User.Configuration;

        if (remember) await ActivateAsync(account).ConfigureAwait(false);
        else
        {
            Current = account;
            SessionChanged?.Invoke();
        }
        return account;
    }

    private async Task ActivateAsync(SavedAccount account)
    {
        account.LastUsed = DateTimeOffset.UtcNow;
        await _accounts.UpsertAsync(account).ConfigureAwait(false);
        _settings.Current.LastAccountId = account.Id;
        await _settings.SaveAsync().ConfigureAwait(false);
        Current = account;
        SessionChanged?.Invoke();
    }

    /// <summary>Leave the current user but keep them in the saved list (quick switch back later).</summary>
    public void SwitchUser()
    {
        Current = null;
        UserConfig = null;
        _client.ClearSession();
        SessionChanged?.Invoke();
    }

    /// <summary>Full sign out: revoke the token on the server and forget the account on this device.</summary>
    public async Task SignOutAndForgetAsync(SavedAccount account)
    {
        if (Current?.Id == account.Id)
        {
            await _client.LogoutAsync().ConfigureAwait(false);
            Current = null;
            UserConfig = null;
            _client.ClearSession();
        }
        await _accounts.RemoveAsync(account.Id).ConfigureAwait(false);
        if (_settings.Current.LastAccountId == account.Id)
        {
            _settings.Current.LastAccountId = null;
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        SessionChanged?.Invoke();
    }
}
