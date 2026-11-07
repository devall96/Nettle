using LibVLCSharp.Shared;

namespace Slopfin.UI.Playback;

/// <summary>
/// Single shared libVLC instance. libVLC decodes everything locally (hardware-accelerated when
/// available), which is why Slopfin never needs the server to transcode.
/// </summary>
public static class PlaybackEngine
{
    private static LibVLC? _vlc;
    private static readonly object Gate = new();

    /// <summary>Extra libVLC options from the platform head (set before first use).</summary>
    public static string[] PlatformOptions { get; set; } = [];

    public static LibVLC Vlc
    {
        get
        {
            lock (Gate)
            {
                if (_vlc != null) return _vlc;
                _vlc = new LibVLC([.. PlatformOptions,
                    "--no-video-title-show",
                    "--no-snapshot-preview",
                    "--no-stats",
                    "--no-sub-autodetect-file",
                    "--no-osd",
                    "--http-reconnect",
                    // Privacy: libVLC never looks up artwork/metadata online, and its Lua
                    // extensions (online playlist resolvers, art finders) are disabled entirely.
                    "--no-metadata-network-access",
                    "--no-lua"]);
                // Don't advertise the exact VLC build to servers.
                _vlc.SetUserAgent("Slopfin", "Slopfin");
                // Only errors go to Slopfin's diagnostic log.
                _vlc.Log += (_, e) =>
                {
                    if (e.Level == LogLevel.Error) Slopfin.Core.Services.Log.Warn($"vlc/{e.Module}: {e.Message}");
                };
                return _vlc;
            }
        }
    }

    /// <summary>Warm up libVLC (loads its plugin cache) off the UI thread so first play is instant.</summary>
    public static void Prewarm() => Task.Run(() => { _ = Vlc; });
}
