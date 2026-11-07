namespace Nettle.UI.Playback;

/// <summary>
/// Creates mpv players with Nettle's settings. mpv plays the original file and decodes it on
/// this device (hardware-accelerated when available), which is why Nettle never needs the
/// server to transcode.
/// </summary>
public static class PlaybackEngine
{
    /// <summary>Extra mpv options from the platform head (e.g. Windows HDR output), applied last.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> PlatformOptions { get; set; } = [];

    /// <summary>Options shared by every platform.</summary>
    public static IEnumerable<KeyValuePair<string, string>> BaseOptions(bool hardwareDecoding)
    {
        KeyValuePair<string, string> O(string k, string v) => new(k, v);

        // Privacy / isolation: never read the user's own mpv config or scripts, never resolve
        // URLs through youtube-dl, never remember positions on disk, generic user agent.
        yield return O("config", "no");
        yield return O("load-scripts", "no");
        yield return O("ytdl", "no");
        yield return O("osc", "no");
        yield return O("load-stats-overlay", "no");
        yield return O("load-osd-console", "no");
        yield return O("load-auto-profiles", "no");
        yield return O("resume-playback", "no");
        yield return O("save-position-on-quit", "no");
        yield return O("user-agent", "Nettle");
        yield return O("audio-client-name", "Nettle");

        // Nettle draws all UI and handles all input itself.
        yield return O("input-default-bindings", "no");
        yield return O("input-vo-keyboard", "no");
        yield return O("input-cursor", "no");
        yield return O("cursor-autohide", "no");
        yield return O("osd-level", "0");
        yield return O("terminal", "no");

        // Playback behaviour.
        yield return O("idle", "yes");
        yield return O("keep-open", "no");
        yield return O("sub-auto", "no");          // only subtitles from the server
        yield return O("audio-file-auto", "no");
        yield return O("volume-max", "130");
        yield return O("hr-seek", "default");
        yield return O("cache", "yes");
        yield return O("demuxer-max-bytes", "400MiB");
        yield return O("network-timeout", "30");
        yield return O("vo", "gpu-next");
        yield return O("hwdec", hardwareDecoding ? "auto-safe" : "no");
        yield return O("video-sync", "display-resample"); // smooth motion, no 24p judder
        yield return O("keepaspect-window", "no");
    }

    /// <summary>
    /// True when video is drawn through mpv's render API into Nettle's own OpenGL surface (so the
    /// controls are ordinary UI on top), instead of mpv drawing into a native child window.
    /// Set by the platform head (Linux).
    /// </summary>
    public static bool UseRenderApi { get; set; }

    /// <summary>Uses an already-loaded libmpv (platform heads that bundle their own copy).</summary>
    public static void UseLibrary(IntPtr handle) => Mpv.LibraryHandle = handle;

    /// <summary>
    /// Audio passthrough: Dolby (AC-3, E-AC-3 / Atmos, TrueHD) and DTS (incl. DTS-HD / DTS:X) go
    /// to the receiver untouched, so it decodes them (and Atmos height channels reach it).
    /// Anything else is decoded as usual.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> PassthroughOptions()
    {
        yield return new("audio-spdif", "ac3,eac3,dts,dts-hd,truehd");
        // Windows only sends TrueHD / DTS-HD bitstreams with exclusive access to the device, so
        // that's requested per file, only for those (see MpvLoad.ExclusiveAudio).
    }

    /// <summary>
    /// True when this audio track can only be bitstreamed with exclusive device access (Windows):
    /// Dolby TrueHD (incl. Atmos) and lossless / high-resolution DTS (DTS-HD MA, DTS:X).
    /// </summary>
    public static bool NeedsExclusiveAudio(string? codec, string? profile)
    {
        var c = codec?.ToLowerInvariant();
        if (c is "truehd" or "mlp") return true;
        if (c is "dts" or "dca")
        {
            var p = profile ?? "";
            return p.Contains("MA", StringComparison.OrdinalIgnoreCase) || p.Contains("HRA", StringComparison.OrdinalIgnoreCase)
                || p.Contains("HD", StringComparison.OrdinalIgnoreCase) || p.Contains("X", StringComparison.Ordinal);
        }
        return false;
    }

    public static MpvPlayer CreatePlayer(bool hardwareDecoding, bool passthrough = false) =>
        new(BaseOptions(hardwareDecoding).Concat(PlatformOptions).Concat(passthrough ? PassthroughOptions() : []));

    /// <summary>Loads libmpv off the UI thread so the first play is instant.</summary>
    public static void Prewarm() => Task.Run(() =>
    {
        try { Mpv.EnsureResolver(); _ = Mpv.mpv_client_api_version(); }
        catch (Exception ex) { Nettle.Core.Services.Log.Error("libmpv failed to load", ex); }
    });

    /// <summary>libmpv's version string, e.g. "mpv v0.41.0" (null if it can't be loaded).</summary>
    public static string? Version
    {
        get
        {
            try
            {
                using var p = new MpvPlayer([new("config", "no"), new("vo", "null"), new("ao", "null")]);
                p.Initialize(null);
                return p.GetProperty("mpv-version");
            }
            catch { return null; }
        }
    }
}
