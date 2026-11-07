using System.Globalization;
using System.Runtime.InteropServices;

namespace Nettle.UI.Playback;

/// <summary>
/// One audio / subtitle track as mpv reports it. <see cref="FfIndex"/> is the stream's index in
/// the file (what Jellyfin calls the stream index); <see cref="ExternalUrl"/> is set for
/// subtitles loaded from a separate file.
/// </summary>
public sealed record MpvTrack(int Id, string Type, string Name, bool Selected, int? FfIndex = null, string? ExternalUrl = null);

/// <summary>What to play and how: everything a single "loadfile" needs.</summary>
public sealed class MpvLoad
{
    public required string Url { get; init; }
    public double? StartSeconds { get; init; }
    public string? AudioLanguage { get; init; }
    public string? SubtitleLanguage { get; init; }
    /// <summary>Default, Always, OnlyForced, Smart or None (Jellyfin's subtitle modes).</summary>
    public string SubtitleMode { get; init; } = "Default";
    public IReadOnlyList<(string Url, string Title, string? Language)> ExternalSubtitles { get; init; } = [];
    /// <summary>Seconds of read-ahead to aim for (live feeds want a bit more).</summary>
    public int CacheSeconds { get; init; } = 10;
    /// <summary>Read-ahead memory cap in bytes, sized from the file's bitrate (null = mpv's default from the options).</summary>
    public long? CacheBytes { get; init; }
    /// <summary>
    /// "Authorization" header value sent with every request for this file and its subtitles
    /// (the token stays out of the URL). Only ever set for the user's own Jellyfin server.
    /// </summary>
    public string? AuthHeader { get; init; }
    /// <summary>Take the audio device exclusively (Windows: needed only to bitstream TrueHD / DTS-HD).</summary>
    public bool ExclusiveAudio { get; init; }
}

/// <summary>A chapter of the playing file (from the file itself).</summary>
public sealed record MpvChapter(double Seconds, string? Title);

/// <summary>
/// One libmpv instance driving one video surface. libmpv's API is thread-safe, so calls are
/// made directly; everything is guarded so nothing touches the handle once it's destroyed.
/// The core is only initialized once the surface hands over its native window ("wid"), so mpv
/// never opens a window of its own. State is cached from mpv's property notifications, so
/// reading it (e.g. from a UI timer) never blocks.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private readonly object _gate = new();
    private IntPtr _h;
    private bool _initialized;
    private bool _terminating;
    private Thread? _events;
    private readonly List<Action<IntPtr>> _pending = new();

    // cached state (written by the event thread)
    private volatile bool _paused, _cacheWait, _fileLoaded, _restarted, _ended;
    private double _timeSec = -1, _durationSec;
    private long _width, _height, _audioRate;
    private long _currentEntry = -1;
    private MpvLoad? _currentLoad;
    private readonly HashSet<string> _loggedThisFile = new();

    /// <summary>First frame of a newly loaded file is ready .</summary>
    public event Action? Playing;
    public event Action? EndReached;
    /// <summary>Loading or playback failed; the argument is mpv's reason.</summary>
    public event Action<string>? Error;

    public MpvPlayer(IEnumerable<KeyValuePair<string, string>> options)
    {
        Mpv.EnsureResolver();
        _h = Mpv.mpv_create();
        if (_h == IntPtr.Zero) throw new InvalidOperationException("libmpv couldn't be started.");
        foreach (var (k, v) in options)
        {
            int r = Mpv.mpv_set_option_string(_h, k, v);
            if (r < 0) Nettle.Core.Services.Log.Warn($"mpv option {k}={v} rejected: {Mpv.ErrorString(r)}");
        }
    }

    // ---------------- lifecycle ----------------

    /// <summary>
    /// Starts the core, drawing into <paramref name="wid"/> (HWND / X11 window), or headless with
    /// null. <paramref name="beforeLoad"/> runs right after the core starts and before any queued
    /// file is loaded (the render API needs its context to exist before video starts).
    /// </summary>
    public void Initialize(long? wid, Action<IntPtr>? beforeLoad = null)
    {
        lock (_gate)
        {
            if (_initialized || _h == IntPtr.Zero) return;
            if (wid is { } w) Mpv.mpv_set_option_string(_h, "wid", w.ToString(CultureInfo.InvariantCulture));
            int r = Mpv.mpv_initialize(_h);
            if (r < 0) throw new InvalidOperationException("libmpv failed to initialize: " + Mpv.ErrorString(r));
            beforeLoad?.Invoke(_h);
            Mpv.mpv_request_log_messages(_h, "warn");
            Observe(1, "time-pos", Mpv.FormatDouble);
            Observe(2, "duration", Mpv.FormatDouble);
            Observe(3, "pause", Mpv.FormatFlag);
            Observe(4, "paused-for-cache", Mpv.FormatFlag);
            Observe(5, "width", Mpv.FormatInt64);
            Observe(6, "height", Mpv.FormatInt64);
            Observe(7, "audio-params/samplerate", Mpv.FormatInt64);
            _initialized = true;
            _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv events" };
            _events.Start();
            foreach (var a in _pending) a(_h);
            _pending.Clear();
        }
    }

    private void Observe(ulong id, string name, int format) => Mpv.mpv_observe_property(_h, id, name, format);

    /// <summary>Runs now if the core is up, otherwise as soon as it is. Never after shutdown.</summary>
    private void Run(Action<IntPtr> action)
    {
        lock (_gate)
        {
            if (_h == IntPtr.Zero || _terminating) return;
            if (!_initialized) { _pending.Add(action); return; }
            try { action(_h); }
            catch (Exception ex) { Nettle.Core.Services.Log.Error("mpv call failed", ex); }
        }
    }

    // A render-API surface owns a render context that must be freed (with its GL context
    // current) before the core is destroyed, so shutdown waits for it to let go.
    private bool _renderAttached, _terminateAfterRender;

    /// <summary>Called by a render-API surface once its render context exists.</summary>
    public void AttachRender() { lock (_gate) _renderAttached = true; }

    /// <summary>Called by a render-API surface after freeing its render context.</summary>
    public void DetachRender()
    {
        bool terminate;
        lock (_gate)
        {
            _renderAttached = false;
            terminate = _terminateAfterRender;
        }
        if (terminate) Terminate();
    }

    /// <summary>Stops playback and frees mpv. Safe to call more than once and from any thread.</summary>
    public void Terminate(int waitMs = 3000)
    {
        Thread? events;
        lock (_gate)
        {
            if (_renderAttached) { _terminateAfterRender = true; return; }
            if (_h == IntPtr.Zero || _terminating) { events = _events; goto wait; }
            _terminating = true;
            _pending.Clear();
            if (!_initialized)
            {
                Mpv.mpv_terminate_destroy(_h);
                _h = IntPtr.Zero;
                return;
            }
            Mpv.Command(_h, "quit"); // the event thread sees SHUTDOWN and destroys the handle
            events = _events;
        }
    wait:
        if (events != null && events != Thread.CurrentThread) events.Join(waitMs);
    }

    public void Dispose() => Task.Run(() => Terminate());

    // ---------------- events ----------------

    private void EventLoop()
    {
        var h = _h;
        while (true)
        {
            var ev = Mpv.mpv_wait_event(h, -1);
            int id = Marshal.ReadInt32(ev);
            if (id == Mpv.EventShutdown) break;
            try { Handle(id, ev); }
            catch (Exception ex) { Nettle.Core.Services.Log.Error("mpv event handling failed", ex); }
        }
        lock (_gate)
        {
            Mpv.mpv_terminate_destroy(h);
            _h = IntPtr.Zero;
        }
    }

    private void Handle(int id, IntPtr ev)
    {
        var data = Marshal.ReadIntPtr(ev, 16);
        switch (id)
        {
            case Mpv.EventStartFile:
                _currentEntry = Marshal.ReadInt64(data);
                _fileLoaded = _restarted = _ended = false;
                _loggedThisFile.Clear();
                break;
            case Mpv.EventFileLoaded:
                _fileLoaded = true;
                AddExternalSubtitles(_currentLoad);
                break;
            case Mpv.EventPlaybackRestart:
                if (_fileLoaded && !_restarted)
                {
                    _restarted = true;
                    Playing?.Invoke();
                }
                break;
            case Mpv.EventEndFile:
            {
                int reason = Marshal.ReadInt32(data);
                int error = Marshal.ReadInt32(data, 4);
                long entry = Marshal.ReadInt64(data, 8);
                if (entry != _currentEntry) break; // a file we already replaced
                if (reason == Mpv.EndReasonEof) { _ended = true; EndReached?.Invoke(); }
                else if (reason == Mpv.EndReasonError) { _ended = true; Error?.Invoke(Mpv.ErrorString(error)); }
                break;
            }
            case Mpv.EventPropertyChange:
                OnProperty(Marshal.ReadInt64(ev, 8), data);
                break;
            case Mpv.EventLogMessage:
            {
                var prefix = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 0));
                var text = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 16))?.TrimEnd();
                int level = Marshal.ReadInt32(data, 24);
                if (!string.IsNullOrEmpty(text))
                {
                    var line = $"mpv/{prefix}: {text}";
                    LogMessage?.Invoke(level, line);
                    // Some files repeat the same warning per frame; log each distinct line once per file.
                    if (_loggedThisFile.Count < 200 && _loggedThisFile.Add(line))
                    {
                        if (level <= 20) Nettle.Core.Services.Log.Warn(line); else Nettle.Core.Services.Log.Info(line);
                    }
                }
                break;
            }
        }
    }

    /// <summary>Raised for each warning/error line from mpv (level: 10 fatal, 20 error, 30 warn).</summary>
    public event Action<int, string>? LogMessage;

    private void OnProperty(long id, IntPtr prop)
    {
        int format = Marshal.ReadInt32(prop, 8);
        var value = Marshal.ReadIntPtr(prop, 16);
        bool has = format != Mpv.FormatNone && value != IntPtr.Zero;
        switch (id)
        {
            case 1: _timeSec = has ? BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value)) : -1; break;
            case 2: _durationSec = has ? BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value)) : 0; break;
            case 3: _paused = has && Marshal.ReadInt32(value) != 0; break;
            case 4: _cacheWait = has && Marshal.ReadInt32(value) != 0; break;
            case 5: _width = has ? Marshal.ReadInt64(value) : 0; break;
            case 6: _height = has ? Marshal.ReadInt64(value) : 0; break;
            case 7: _audioRate = has ? Marshal.ReadInt64(value) : 0; break;
        }
    }

    // ---------------- state ----------------

    public long TimeMs => _timeSec < 0 ? -1 : (long)(_timeSec * 1000);
    public long LengthMs => (long)(_durationSec * 1000);
    public bool IsPaused => _paused;
    /// <summary>A file is loaded, its first frame has been shown, and it's neither paused nor finished.</summary>
    public bool IsPlaying => _restarted && !_paused && !_ended;
    public bool HasEnded => _ended;
    /// <summary>Opening, or stalled waiting for the network.</summary>
    public bool IsBuffering => (_currentLoad != null && !_restarted && !_ended) || _cacheWait;
    /// <summary>Decoded picture size (0 until the first frame is decoded).</summary>
    public (int Width, int Height) VideoSize => ((int)_width, (int)_height);
    /// <summary>True once audio is actually being decoded and sent to an output.</summary>
    public bool HasActiveAudio => _audioRate > 0;

    /// <summary>A property as a string (null if unavailable). Thread-safe; briefly waits on the core.</summary>
    public string? GetProperty(string name)
    {
        string? result = null;
        lock (_gate)
            if (_initialized && _h != IntPtr.Zero && !_terminating) result = Mpv.GetString(_h, name);
        return result;
    }

    // ---------------- control ----------------

    public void Load(MpvLoad load)
    {
        _currentLoad = load;
        _restarted = _fileLoaded = _ended = false;
        _timeSec = -1; _durationSec = 0; _width = _height = _audioRate = 0;
        Run(h =>
        {
            // Per-file settings are applied as global options right before loading (mpv reads
            // them when the file starts), which works across all libmpv versions.
            Mpv.mpv_set_property_string(h, "start", load.StartSeconds is > 0.5 ? load.StartSeconds.Value.ToString("0.###", CultureInfo.InvariantCulture) : "none");
            Mpv.mpv_set_property_string(h, "alang", load.AudioLanguage ?? "");
            ApplySubtitleMode(h, load.SubtitleMode, load.SubtitleLanguage);
            Mpv.mpv_set_property_string(h, "cache-secs", load.CacheSeconds.ToString(CultureInfo.InvariantCulture));
            if (load.CacheBytes is { } bytes)
                Mpv.mpv_set_property_string(h, "demuxer-max-bytes", bytes.ToString(CultureInfo.InvariantCulture));
            // One header, no commas (mpv splits this list on commas). Empty for other hosts.
            Mpv.mpv_set_property_string(h, "http-header-fields", load.AuthHeader != null ? "Authorization: " + load.AuthHeader : "");
            if (OperatingSystem.IsWindows())
                Mpv.mpv_set_property_string(h, "audio-exclusive", load.ExclusiveAudio ? "yes" : "no");
            // Per-file fine tuning starts from zero again (speed and subtitle size carry over).
            Mpv.mpv_set_property_string(h, "sub-delay", "0");
            Mpv.mpv_set_property_string(h, "audio-delay", "0");
            Mpv.mpv_set_property_string(h, "pause", "no");
            int r = Mpv.Command(h, "loadfile", load.Url, "replace");
            if (r < 0) Error?.Invoke(Mpv.ErrorString(r));
        });
    }

    private void AddExternalSubtitles(MpvLoad? load)
    {
        if (load == null) return;
        foreach (var (url, title, lang) in load.ExternalSubtitles)
            Run(h => Mpv.Command(h, "sub-add", url, "auto", title, lang ?? ""));
    }

    /// <summary>
    /// Jellyfin's subtitle modes in mpv terms. Options a given libmpv doesn't know are simply
    /// rejected (older builds then behave as "Default").
    /// </summary>
    private static void ApplySubtitleMode(IntPtr h, string mode, string? language)
    {
        string sid = "auto", fallback = "default", forced = "yes", matching = "yes", slang = language ?? "";
        switch (mode)
        {
            case "None": sid = "no"; break;
            case "Always": fallback = "yes"; break;
            // Only forced (foreign-dialogue) tracks: no language match, no default track, forced always.
            case "OnlyForced": slang = ""; fallback = "no"; forced = "always"; break;
            // Subtitles only when the audio is in another language than the subtitles.
            case "Smart": matching = "no"; break;
        }
        Mpv.mpv_set_property_string(h, "slang", slang);
        Mpv.mpv_set_property_string(h, "sid", sid);
        Mpv.mpv_set_property_string(h, "subs-fallback", fallback);
        Mpv.mpv_set_property_string(h, "subs-fallback-forced", forced);
        Mpv.mpv_set_property_string(h, "subs-with-matching-audio", matching);
    }

    // UI actions use mpv's asynchronous calls: they return at once even while mpv is busy (e.g.
    // waiting on the network), so a click or key press never stalls the interface.

    public void SetPause(bool pause) => Run(h => Mpv.SetAsync(h, "pause", pause ? "yes" : "no"));

    public void SeekTo(long ms) =>
        Run(h => Mpv.CommandAsync(h, "seek", (Math.Max(0, ms) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture), "absolute"));

    public void Stop() => Run(h => Mpv.CommandAsync(h, "stop"));

    public void SetVolume(int volume) => Run(h => Mpv.SetAsync(h, "volume", Math.Clamp(volume, 0, 130).ToString(CultureInfo.InvariantCulture)));

    public void SetMute(bool mute) => Run(h => Mpv.SetAsync(h, "mute", mute ? "yes" : "no"));

    public void SetHardwareDecoding(bool on) => Run(h => Mpv.SetAsync(h, "hwdec", on ? "auto-safe" : "no"));

    /// <summary>Audio track id, or -1 to turn subtitles off for <paramref name="type"/> "sub".</summary>
    public void SelectTrack(string type, int id) =>
        Run(h => Mpv.SetAsync(h, type == "audio" ? "aid" : "sid", id < 0 ? "no" : id.ToString(CultureInfo.InvariantCulture)));

    public void SetSpeed(double speed) => Run(h => Mpv.SetAsync(h, "speed", speed.ToString("0.###", CultureInfo.InvariantCulture)));

    /// <summary>Subtitle timing offset in seconds (positive = later).</summary>
    public void SetSubtitleDelay(double seconds) => Run(h => Mpv.SetAsync(h, "sub-delay", seconds.ToString("0.###", CultureInfo.InvariantCulture)));

    /// <summary>Audio timing offset in seconds (positive = later).</summary>
    public void SetAudioDelay(double seconds) => Run(h => Mpv.SetAsync(h, "audio-delay", seconds.ToString("0.###", CultureInfo.InvariantCulture)));

    public void SetSubtitleScale(double scale) => Run(h => Mpv.SetAsync(h, "sub-scale", scale.ToString("0.###", CultureInfo.InvariantCulture)));

    /// <summary>Adds a subtitle file while playing and selects it.</summary>
    public void AddSubtitle(string url, string title, string? language) =>
        Run(h => Mpv.CommandAsync(h, "sub-add", url, "select", title, language ?? ""));

    /// <summary>The file's chapters (empty when it has none).</summary>
    public IReadOnlyList<MpvChapter> GetChapters()
    {
        var list = new List<MpvChapter>();
        lock (_gate)
        {
            if (!_initialized || _h == IntPtr.Zero || _terminating) return list;
            if (!int.TryParse(Mpv.GetString(_h, "chapter-list/count"), out var n)) return list;
            for (int i = 0; i < n; i++)
            {
                if (!double.TryParse(Mpv.GetString(_h, $"chapter-list/{i}/time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
                var title = Mpv.GetString(_h, $"chapter-list/{i}/title");
                list.Add(new MpvChapter(t, string.IsNullOrWhiteSpace(title) ? null : title));
            }
        }
        return list;
    }

    /// <summary>Saves the current frame (as decoded, before any overlay) to a PNG/JPG file.</summary>
    public bool Screenshot(string path)
    {
        int r = -1;
        Run(h => r = Mpv.Command(h, "screenshot-to-file", path, "video"));
        return r >= 0;
    }

    public IReadOnlyList<MpvTrack> GetTracks()
    {
        var list = new List<MpvTrack>();
        lock (_gate)
        {
            if (!_initialized || _h == IntPtr.Zero || _terminating) return list;
            if (!int.TryParse(Mpv.GetString(_h, "track-list/count"), out var n)) return list;
            for (int i = 0; i < n; i++)
            {
                string P(string f) => Mpv.GetString(_h, $"track-list/{i}/{f}") ?? "";
                var type = P("type");
                if (type is not ("audio" or "sub") || !int.TryParse(P("id"), out var tid)) continue;
                bool external = P("external") == "yes";
                int? ff = int.TryParse(P("ff-index"), out var f) ? f : null;
                list.Add(new MpvTrack(tid, type, DescribeTrack(P("title"), P("lang"), P("codec"), P("demux-channel-count"), external, type == "sub" && P("forced") == "yes"),
                    P("selected") == "yes", external ? null : ff, external ? P("external-filename") : null));
            }
        }
        return list;
    }

    private static string DescribeTrack(string title, string lang, string codec, string channels, bool external, bool forced)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        if (!string.IsNullOrEmpty(lang) && !title.Contains(lang, StringComparison.OrdinalIgnoreCase))
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
