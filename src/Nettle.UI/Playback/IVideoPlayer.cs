using System.Globalization;

namespace Nettle.UI.Playback;

/// <summary>
/// A playback engine as the player screen sees it. Desktop uses mpv (<see cref="MpvPlayer"/>);
/// Android TV uses Media3 / ExoPlayer. Every engine is only ever handed the original file's
/// direct-play URL: none of them may ask the server to transcode.
/// State getters must be cheap and safe to call from any thread (they're read from UI timers
/// and background tasks); commands may be queued to the engine's own thread.
/// </summary>
public interface IVideoPlayer : IDisposable
{
    event Action? Playing;
    event Action? EndReached;
    event Action<string>? Error;

    long TimeMs { get; }
    long LengthMs { get; }
    bool IsPaused { get; }
    bool IsPlaying { get; }
    bool HasEnded { get; }
    bool IsBuffering { get; }
    (int Width, int Height) VideoSize { get; }
    bool HasActiveAudio { get; }

    /// <summary>Signal sent to the display: "pq" or "hlg" for HDR, anything else (or null) for SDR.</summary>
    string? HdrOutputSignal { get; }
    /// <summary>True when the audio bitstream goes to the TV / receiver undecoded.</summary>
    bool IsAudioPassthrough { get; }
    /// <summary>One line on how the file is really being played (decoder, outputs), for the log.</summary>
    string DescribeOutput();
    /// <summary>What this engine can do, so the player screen hides what it can't.</summary>
    PlayerFeatures Features { get; }
    /// <summary>The screen's refresh rate right now (Hz), when the engine knows it; Audio timing is kept per rate.</summary>
    double? DisplayFps => null;

    void Load(PlayerLoad load);
    void SetPause(bool pause);
    void SeekTo(long ms);
    void Stop();
    void SetVolume(int volume);
    void SetMute(bool mute);
    /// <summary>Track id from <see cref="GetTracks"/>, or -1 to turn subtitles off for "sub".</summary>
    void SelectTrack(string type, int id);
    void SetSpeed(double speed);
    void SetSubtitleDelay(double seconds);
    void SetAudioDelay(double seconds);
    void SetSubtitleScale(double scale);
    void AddSubtitle(string url, string title, string? language);
    IReadOnlyList<PlayerChapter> GetChapters();
    IReadOnlyList<PlayerTrack> GetTracks();
}

[Flags]
public enum PlayerFeatures
{
    None = 0,
    AudioDelay = 1,
    SubtitleDelay = 2,
    /// <summary>Volume above 100% (software boost).</summary>
    VolumeBoost = 4,
    /// <summary>Chapters read from the file itself.</summary>
    FileChapters = 8,
    /// <summary>Applies Dolby Vision picture data itself (mpv / libplacebo) when outputting PQ.</summary>
    DolbyVisionMapping = 16,
    All = AudioDelay | SubtitleDelay | VolumeBoost | FileChapters | DolbyVisionMapping,
}

/// <summary>
/// One audio / subtitle track. <see cref="FfIndex"/> is the stream's index in the file (what
/// Jellyfin calls the stream index); <see cref="ExternalUrl"/> is set for subtitles loaded from
/// a separate file.
/// </summary>
public sealed record PlayerTrack(int Id, string Type, string Name, bool Selected, int? FfIndex = null, string? ExternalUrl = null);

/// <summary>What to play and how.</summary>
public sealed record PlayerLoad
{
    /// <summary>The original file's direct-play URL (never a transcode / HLS URL).</summary>
    public required string Url { get; init; }
    public double? StartSeconds { get; init; }
    public string? AudioLanguage { get; init; }
    public string? SubtitleLanguage { get; init; }
    /// <summary>Default, Always, OnlyForced, Smart or None (Jellyfin's subtitle modes).</summary>
    public string SubtitleMode { get; init; } = "Default";
    public IReadOnlyList<(string Url, string Title, string? Language)> ExternalSubtitles { get; init; } = [];
    /// <summary>Seconds of read-ahead to aim for (live feeds want a bit more).</summary>
    public int CacheSeconds { get; init; } = 10;
    /// <summary>Read-ahead memory cap in bytes, sized from the file's bitrate (null = engine default).</summary>
    public long? CacheBytes { get; init; }
    /// <summary>
    /// "Authorization" header value sent with every request for this file and its subtitles
    /// (the token stays out of the URL). Only ever set for the user's own Jellyfin server.
    /// </summary>
    public string? AuthHeader { get; init; }
    /// <summary>Take the audio device exclusively (Windows: needed only to bitstream TrueHD / DTS-HD).</summary>
    public bool ExclusiveAudio { get; init; }
    /// <summary>Open paused at the start position (watch together: the group starts once everyone is ready).</summary>
    public bool StartPaused { get; init; }
    /// <summary>Play the file over and over (Settings → Lip sync check).</summary>
    public bool Loop { get; init; }
}

/// <summary>A chapter of the playing file.</summary>
public sealed record PlayerChapter(double Seconds, string? Title);

/// <summary>Display text shared by the engines.</summary>
public static class PlayerText
{
    public static string DescribeTrack(string title, string lang, string codec, string channels, bool external, bool forced)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        if (!string.IsNullOrEmpty(lang) && lang != "und" && !title.Contains(lang, StringComparison.OrdinalIgnoreCase))
        {
            try { parts.Add(new CultureInfo(lang).EnglishName); } catch { parts.Add(lang.ToUpperInvariant()); }
        }
        var tech = new List<string>();
        if (!string.IsNullOrEmpty(codec)) tech.Add(codec.ToUpperInvariant());
        if (int.TryParse(channels, out var ch) && ch > 0) tech.Add(ch switch { 1 => "mono", 2 => "stereo", 6 => "5.1", 8 => "7.1", _ => ch + "ch" });
        if (forced) tech.Add("forced");
        if (external) tech.Add("external");
        var name = parts.Count > 0 ? string.Join(" · ", parts) : "Track";
        return tech.Count > 0 ? $"{name}  ({string.Join(", ", tech)})" : name;
    }
}
