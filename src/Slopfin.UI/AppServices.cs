using Slopfin.Core.Api;
using Slopfin.Core.Platform;
using Slopfin.Core.Services;
using Slopfin.UI.Services;
using Slopfin.UI.ViewModels;

namespace Slopfin.UI;

/// <summary>
/// Tiny service registry. Platform heads call <see cref="Initialize"/> with their own
/// secure store / platform info, everything else is shared.
/// </summary>
public static class AppServices
{
    /// <summary>App version, taken from the build (Directory.Build.props &lt;Version&gt;).</summary>
    public static readonly string Version = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static IPlatformInfo Platform { get; private set; } = null!;
    public static ISecureStore Secure { get; private set; } = null!;
    public static IWindowHost? Window { get; set; }
    public static IUpdater? Updater { get; set; }
    /// <summary>Applications-menu integration (Linux AppImage); null where the installer already handles it.</summary>
    public static IDesktopIntegration? Desktop { get; set; }
    public static SettingsStore Settings { get; private set; } = null!;
    public static AccountStore Accounts { get; private set; } = null!;
    public static JellyfinClient Jellyfin { get; private set; } = null!;
    public static SessionService Session { get; private set; } = null!;
    public static SegmentService Segments { get; private set; } = null!;
    public static NtvItemResolver NtvItems { get; private set; } = null!;
    public static SeerrClient Seerr { get; private set; } = null!;
    public static NtvClient Ntv { get; private set; } = null!;
    public static ImageCache Images { get; private set; } = null!;
    public static MainViewModel Main { get; set; } = null!;

    public static void Initialize(IPlatformInfo platform, ISecureStore secure)
    {
        Platform = platform;
        Secure = secure;
        Settings = new SettingsStore(platform);
        Settings.Load();
        Accounts = new AccountStore(secure);
        Jellyfin = new JellyfinClient(HttpFactory.Create(), platform.DeviceName, Version);
        Session = new SessionService(Jellyfin, Accounts, Settings);
        Segments = new SegmentService(Jellyfin);
        NtvItems = new NtvItemResolver(Jellyfin);
        Seerr = new SeerrClient();
        Ntv = new NtvClient();
        Images = new ImageCache(Path.Combine(platform.CacheDirectory, "images")) { IsAllowed = IsAllowedHost };
    }

    /// <summary>
    /// The only hosts Slopfin will ever fetch images from: servers the user configured
    /// (Jellyfin, saved accounts' servers, NostalgiaTV, Seerr), plus TMDB only if opted in.
    /// </summary>
    public static bool IsAllowedHost(Uri uri)
    {
        static bool Same(Uri u, string? baseUrl) =>
            baseUrl != null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) && string.Equals(b.Host, u.Host, StringComparison.OrdinalIgnoreCase);

        if (Same(uri, Jellyfin.BaseUrl) || Same(uri, Ntv.BaseUrl) || Same(uri, Seerr.BaseUrl)) return true;
        foreach (var a in Session.SavedAccounts) if (Same(uri, a.ServerUrl)) return true;
        if (Settings.Current.AllowDirectTmdbImages && uri.Host.Equals("image.tmdb.org", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Loads secrets for optional integrations (Seerr / NostalgiaTV).</summary>
    public static async Task LoadIntegrationsAsync()
    {
        var s = Settings.Current;
        Seerr.Configure(s.SeerrUrl, await Secure.GetAsync("seerr.apikey"), await Secure.GetAsync("seerr.cookie"));
        Seerr.AllowDirectTmdbImages = s.AllowDirectTmdbImages;
        Ntv.Configure(s.NtvUrl, await Secure.GetAsync("ntv.apikey"));
    }
}

/// <summary>Window-level operations the shared UI needs from the platform head.</summary>
public interface IWindowHost
{
    bool IsFullScreen { get; }
    void SetFullScreen(bool on);
    /// <summary>Whether the monitor showing the app is currently in HDR mode.</summary>
    HdrDisplayState GetHdrState();
}

public enum HdrDisplayState { Unknown, Sdr, Hdr }

/// <summary>
/// Adds Slopfin to the desktop's applications menu (Linux: a .desktop entry + icon in the user's
/// own ~/.local/share). Only ever done when the user asks.
/// </summary>
public interface IDesktopIntegration
{
    bool IsInMenu { get; }
    /// <summary>True when it's worth asking on first run (running as the downloaded AppImage).</summary>
    bool OfferOnFirstRun { get; }
    void AddToMenu();
    void RemoveFromMenu();
}

/// <summary>
/// App updates (Windows: Velopack). Never contacts anything unless the user presses
/// "Check now" or turns on automatic checks; then it only talks to the update source they set.
/// </summary>
public interface IUpdater
{
    /// <summary>False when running from a plain folder/zip instead of the installed app.</summary>
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    /// <summary>Returns the newer version string, or null when up to date.</summary>
    Task<string?> CheckAsync(string source, CancellationToken ct = default);
    /// <summary>Downloads the pending update (progress 0-100), then restarts into it.</summary>
    Task DownloadAndRestartAsync(Action<int>? progress = null);
}
