using Nettle.Core.Api;
using Nettle.Core.Platform;
using Nettle.Core.Services;
using Nettle.UI.Services;
using Nettle.UI.ViewModels;

namespace Nettle.UI;

/// <summary>
/// Tiny service registry. Platform heads call <see cref="Initialize"/> with their own
/// secure store / platform info, everything else is shared.
/// </summary>
public static class AppServices
{
    /// <summary>App version, taken from the build (Directory.Build.props &lt;Version&gt;).</summary>
    public static readonly string Version = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Optional release label from the build (Directory.Build.props &lt;NettleReleaseLabel&gt;), e.g. "parity hotfix".</summary>
    public static readonly string ReleaseLabel =
        typeof(AppServices).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NettleReleaseLabel")?.Value?.Trim() ?? "";

    /// <summary>"0.4.3" or "0.4.3 · parity hotfix".</summary>
    public static string VersionWithLabel => ReleaseLabel.Length > 0 ? $"{Version} · {ReleaseLabel}" : Version;

    public static IPlatformInfo Platform { get; private set; } = null!;
    /// <summary>Per-platform wording and switches (set by the head before the UI starts).</summary>
    public static PlatformProfile Profile { get; set; } = PlatformProfile.Desktop();
    public static ISecureStore Secure { get; private set; } = null!;
    public static IWindowHost? Window { get; set; }
    public static IUpdater? Updater { get; set; }
    /// <summary>Applications-menu integration (Linux AppImage); null where the installer already handles it.</summary>
    public static IDesktopIntegration? Desktop { get; set; }
    /// <summary>Keeps the screen on while video plays (null where nothing is needed).</summary>
    public static IKeepAwake? KeepAwake { get; set; }
    /// <summary>TV: go back to the launcher (Back pressed on Home). Null on desktop.</summary>
    public static Action? LeaveApp { get; set; }
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
    /// <summary>The server WebSocket: remote control from other Jellyfin apps, live updates.</summary>
    public static RemoteControl Remote { get; private set; } = null!;
    /// <summary>Display scaling of the main window (2 on a 4K screen at 200%), for sizing images.</summary>
    public static double UiScale { get; set; } = 1;

    private static double _contentScale = 1;
    /// <summary>
    /// Desktop: how much the whole UI is shrunk to fit a small window (1 = full size). Windows
    /// that sit over the video (the player controls) follow it too.
    /// </summary>
    public static double ContentScale
    {
        get => _contentScale;
        set { if (Math.Abs(_contentScale - value) < 0.0001) return; _contentScale = value; ContentScaleChanged?.Invoke(value); }
    }
    public static event Action<double>? ContentScaleChanged;

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
        Remote = new RemoteControl(Jellyfin);
        Images = new ImageCache(Path.Combine(platform.CacheDirectory, "images")) { IsAllowed = IsAllowedHost,
            AuthHeaderFor = u => Jellyfin.IsServerUrl(u.AbsoluteUri) ? Jellyfin.TokenHeaderValue : null };
    }

    /// <summary>
    /// The only hosts Nettle will ever fetch images from: servers the user configured
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
    /// <summary>On: the full-screen type chosen in Settings (borderless or exclusive). Off: windowed.</summary>
    void SetFullScreen(bool on);
    /// <summary>Whether the monitor showing the app is currently in HDR mode.</summary>
    HdrDisplayState GetHdrState();

    /// <summary>Windowed / borderless / exclusive switching is available (desktop, not a TV).</summary>
    bool SupportsDisplayModes => false;
    Nettle.Core.Models.DisplayMode DisplayMode => IsFullScreen ? Nettle.Core.Models.DisplayMode.Borderless : Nettle.Core.Models.DisplayMode.Windowed;
    void SetDisplayMode(Nettle.Core.Models.DisplayMode mode) => SetFullScreen(mode != Nettle.Core.Models.DisplayMode.Windowed);
    /// <summary>Frame rate of the video now playing (null when none), for exclusive full screen's refresh-rate matching.</summary>
    void SetVideoFrameRate(double? fps) { }
}

public enum HdrDisplayState { Unknown, Sdr, Hdr }

/// <summary>
/// Stops the screen from blanking / locking while video plays. mpv does this itself only when it
/// owns a native window; with the render API (Linux) the app has to ask the desktop.
/// </summary>
public interface IKeepAwake
{
    /// <summary>True while something is playing; false when paused, stopped or closed. Call on the UI thread.</summary>
    void SetActive(bool on);
}

/// <summary>
/// Adds Nettle to the desktop's applications menu (Linux: a .desktop entry + icon in the user's
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
