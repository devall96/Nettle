using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;
using Nettle.Core.Services;
using Nettle.UI.Playback;

namespace Nettle.UI.ViewModels;

/// <summary>One TV-style format badge. <see cref="Verified"/> = false means "source has it, but it isn't reaching you".</summary>
public sealed class FormatBadgeViewModel(string label, string detail, bool verified)
{
    public string Label => label;
    public string Detail => detail;
    public bool Verified => verified;
    public bool NotVerified => !verified;
}

public sealed partial class TrackOptionViewModel(PlayerViewModel owner, int id, string name, bool isAudio, int? streamIndex = null) : ObservableObject
{
    public int Id => id;
    public string Name => name;
    /// <summary>Jellyfin's index for this stream (-1 for "subtitles off"; null if unknown).</summary>
    public int? StreamIndex => streamIndex;
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Select() => owner.SelectTrack(this, isAudio);
}

/// <summary>
/// Player for both library items (direct play of the original file) and NostalgiaTV live
/// channels, played by mpv. Position is polled on a UI timer from mpv's cached state (cheap,
/// and never blocks on the player).
/// </summary>
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _tick;
    private readonly PlaybackSettings _settings = AppServices.Settings.Current.Playback;
    private string? _currentUrl;
    private bool _disposed;
    private bool _wasFullScreen;

    // Library playback state
    private BaseItemDto? _item;
    private string? _mediaSourceId;
    private string? _playSessionId;
    private long? _startTicks;
    private List<PlaybackSegment> _segments = new();
    private readonly HashSet<PlaybackSegment> _autoSkipped = new();
    private PlaybackSegment? _dismissedSegment;
    private BaseItemDto? _nextEpisode;
    private bool _upNextDismissed;
    private DateTime _lastReport = DateTime.MinValue;
    private DateTime _lastInput = DateTime.UtcNow;
    private DateTime _upNextShownAt;

    // Live state
    private IReadOnlyList<NtvChannel>? _channels;
    private int _channelIndex;
    private DateTime _bannerUntil;

    public MpvPlayer Player { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _sourceLabel;
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private string? _logoUrl;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isBuffering = true;
    [ObservableProperty] private bool _isOsdVisible = true;
    [ObservableProperty] private double _lengthMs = 1;
    [ObservableProperty] private double _seekValue;
    [ObservableProperty] private bool _isSeeking;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private string? _endsAtText;
    [ObservableProperty] private int _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isTracksOpen;
    [ObservableProperty] private string? _skipLabel;
    [ObservableProperty] private bool _isSkipVisible;
    [ObservableProperty] private bool _isUpNextVisible;
    [ObservableProperty] private string? _upNextTitle;
    [ObservableProperty] private string? _upNextImage;
    [ObservableProperty] private int _upNextCountdown;
    [ObservableProperty] private bool _isLive;
    [ObservableProperty] private string? _nowTitle;
    [ObservableProperty] private string? _nowTime;
    [ObservableProperty] private string? _nextTitle;
    [ObservableProperty] private double _liveProgress;
    [ObservableProperty] private bool _isChannelBannerVisible;
    [ObservableProperty] private string? _channelNumber;

    public ObservableCollection<TrackOptionViewModel> AudioTracks { get; } = new();

    // ===================== TV-style format badges =====================

    public ObservableCollection<FormatBadgeViewModel> FormatBadges { get; } = new();
    [ObservableProperty] private bool _isFormatBadgesVisible;
    private List<MediaStream>? _sourceStreams;
    private int _mediaGeneration;
    private CancellationTokenSource? _badgeHideCts;

    /// <summary>
    /// Pops up badges for what is ACTUALLY being output, verified at runtime:
    ///  • resolution from the frames mpv is really decoding,
    ///  • HDR only if the file is HDR AND mpv is sending an HDR (PQ/HLG) signal to the display
    ///    AND (on Windows) the OS reports this monitor in HDR mode,
    ///  • premium audio only once mpv is actually decoding an audio track (stated as decoded).
    /// </summary>
    private async Task ShowFormatBadgesAsync(int tuneVersion, int generation, int delayMs = 1500)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        int w = 0, h = 0;
        for (int i = 0; i < 6 && w == 0; i++)
        {
            if (_disposed || tuneVersion != _tuneVersion || generation != _mediaGeneration) return;
            (w, h) = Player.VideoSize;
            if (w == 0) await Task.Delay(500);
        }
        if (w == 0) return; // no picture yet: claim nothing

        var badges = new List<FormatBadgeViewModel>();
        var video = _sourceStreams?.FirstOrDefault(s => s.Type == "Video");

        var res = VideoFormat.Resolution(w);
        if (res is "4K" or "1440p" or "1080p")
            badges.Add(new FormatBadgeViewModel(res, $"{w}×{h} decoded", true));

        var hdr = VideoFormat.HdrLabel(video?.VideoRangeType, out var hdrWarning);
        if (hdr != null)
        {
            var display = AppServices.Window?.GetHdrState() ?? HdrDisplayState.Unknown;
            // What mpv is really sending to the screen: "pq" / "hlg" = HDR signal.
            var target = await Task.Run(() => Player.GetProperty("video-target-params/gamma"));
            bool hdrOut = target is "pq" or "hlg";
            bool isDv = hdr.StartsWith("Dolby Vision");
            string signal = target == "hlg" ? "HLG" : "HDR10 (PQ)";
            if (hdrWarning != null)
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), hdrWarning, false));
            else if (hdrOut && display != HdrDisplayState.Sdr)
                badges.Add(isDv
                    ? new FormatBadgeViewModel("DOLBY VISION", $"Dolby Vision picture data applied, output as {signal} to your HDR display", true)
                    : new FormatBadgeViewModel(hdr.ToUpperInvariant(), $"HDR output active on this display ({signal})", true));
            else if (display == HdrDisplayState.Hdr)
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), "Your display is in HDR, but this is being tone-mapped to SDR", false));
            else if (display == HdrDisplayState.Sdr || !OperatingSystem.IsWindows())
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), OperatingSystem.IsWindows()
                    ? "Display is in SDR, so this is tone-mapped. Turn on Windows HDR for true HDR."
                    : "Tone-mapped to SDR (HDR output isn't available on Linux yet)", false));
            else
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), "Couldn't confirm your display's HDR mode", false));
        }

        // Audio: only once mpv is decoding an audio track (or passing it through).
        bool audioPlaying = Player.HasActiveAudio;
        var audio = PickAudioStream();
        var audioLabel = VideoFormat.AudioLabel(audio);
        if (audioPlaying && audioLabel != null)
        {
            // mpv reports an "spdif-…" output format when the bitstream goes to the receiver untouched.
            var outFormat = await Task.Run(() => Player.GetProperty("audio-out-params/format"));
            bool passthrough = outFormat?.StartsWith("spdif", StringComparison.OrdinalIgnoreCase) == true;
            var layout = VideoFormat.ChannelLayout(audio!.Channels);
            var detail = passthrough ? "Passed through untouched: your receiver decodes it"
                : audioLabel is "Dolby Atmos" or "DTS:X"
                ? $"Decoded by Nettle to {layout} (turn on audio passthrough in Settings to send height channels to a receiver)"
                : $"Decoded by Nettle · {layout}";
            badges.Add(new FormatBadgeViewModel(audioLabel.ToUpperInvariant(), detail, true));
        }

        if (generation != _mediaGeneration || _disposed) return;
        FormatBadges.Clear();
        foreach (var b in badges) FormatBadges.Add(b);
        Nettle.Core.Services.Log.Info("format badges: " + (badges.Count == 0 ? "(none)" : string.Join(" | ", badges.Select(b => $"{b.Label} [{b.Detail}]"))));
        if (badges.Count == 0) return;

        IsFormatBadgesVisible = true;
        _badgeHideCts?.Cancel();
        var cts = _badgeHideCts = new CancellationTokenSource();
        try { await Task.Delay(7000, cts.Token); } catch (TaskCanceledException) { return; }
        IsFormatBadgesVisible = false;
    }

    /// <summary>The audio stream mpv most likely selected (preferred language, else default, else first).</summary>
    private MediaStream? PickAudioStream()
    {
        var audio = _sourceStreams?.Where(s => s.Type == "Audio").ToList();
        if (audio == null || audio.Count == 0) return null;
        var lang = AudioLang;
        return (!string.IsNullOrEmpty(lang) ? audio.FirstOrDefault(a => string.Equals(a.Language, lang, StringComparison.OrdinalIgnoreCase)) : null)
               ?? audio.FirstOrDefault(a => a.IsDefault) ?? audio[0];
    }
    public ObservableCollection<TrackOptionViewModel> SubtitleTracks { get; } = new();
    public bool HasNext => _nextEpisode != null;
    public bool IsVod => !IsLive;

    /// <summary>False when the user turned on Reduce motion (fades and slides are skipped).</summary>
    public bool MotionEnabled { get; } = !AppServices.Settings.Current.ReduceMotion;

    private PlayerViewModel(MainViewModel main)
    {
        _main = main;
        Player = PlaybackEngine.CreatePlayer(_settings.HardwareDecoding, _settings.AudioPassthrough);
        _volume = Math.Clamp(_settings.Volume, 0, 125);
        Player.SetVolume(_volume);
        Player.EndReached += () => Dispatcher.UIThread.Post(OnEnded);
        Player.Error += reason =>
        {
            var tuneAtError = _tuneVersion;
            Dispatcher.UIThread.Post(() => _ = OnPlaybackErrorAsync(tuneAtError, reason));
        };
        Player.Playing += () =>
        {
            // One line per file on how it's really being played (decoder, output), for bug reports.
            var (w, h) = Player.VideoSize;
            Nettle.Core.Services.Log.Info($"playing: {w}x{h}, hwdec {Player.GetProperty("hwdec-current") ?? "no"}, vo {Player.GetProperty("current-vo")}, " +
                $"audio {Player.GetProperty("audio-codec-name")} → {Player.GetProperty("audio-out-params/format") ?? "?"} via {Player.GetProperty("current-ao")}");
            Dispatcher.UIThread.Post(() =>
            {
                IsBuffering = false;
                LoadTracks();
                LoadChapters();
                _ = ShowFormatBadgesAsync(_tuneVersion, _mediaGeneration);
            });
        };
        // Speed and subtitle size carry over between files and sessions.
        _playbackSpeed = Math.Clamp(_settings.PlaybackSpeed, 0.25, 3);
        _subtitleScale = Math.Clamp(_settings.SubtitleScale, 0.5, 2.5);
        if (Math.Abs(_playbackSpeed - 1) > 0.001) Player.SetSpeed(_playbackSpeed);
        if (Math.Abs(_subtitleScale - 1) > 0.001) Player.SetSubtitleScale(_subtitleScale);

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => OnTick());
        _tick.Start();

        _wasFullScreen = AppServices.Window?.IsFullScreen ?? false;
        AppServices.Window?.SetFullScreen(true);
    }

    partial void OnIsLiveChanged(bool value) => OnPropertyChanged(nameof(IsVod));

    // Screen stays on only while something is actually playing (paused = the screensaver may run).
    partial void OnIsPlayingChanged(bool value) { if (!_disposed) AppServices.KeepAwake?.SetActive(value); }

    // ===================== Library item =====================

    public static PlayerViewModel ForItem(MainViewModel main, BaseItemDto item, long? startTicks, string? mediaSourceId)
    {
        var vm = new PlayerViewModel(main);
        _ = vm.StartItemAsync(item, startTicks, mediaSourceId);
        return vm;
    }

    /// <summary>Plays a list in order (Play all, Shuffle, or a remote control's queue), from <paramref name="index"/>.</summary>
    public static PlayerViewModel ForQueue(MainViewModel main, IReadOnlyList<BaseItemDto> items, int index, long? startTicks = null)
    {
        var vm = new PlayerViewModel(main);
        vm._queue = items.ToList();
        vm._queueIndex = Math.Clamp(index, 0, items.Count - 1);
        _ = vm.StartItemAsync(vm._queue[vm._queueIndex], startTicks, null);
        return vm;
    }

    // ===================== Play queue =====================

    private List<BaseItemDto>? _queue;
    private int _queueIndex;
    public string NextTip => _queue != null ? "Next (N)" : "Next episode (N)";

    /// <summary>Adds items right after the current one ("play next") or at the end (remote control).</summary>
    public void Enqueue(IReadOnlyList<BaseItemDto> items, bool next)
    {
        if (items.Count == 0) return;
        if (_queue == null)
        {
            _queue = _item != null ? new List<BaseItemDto> { _item } : new List<BaseItemDto>();
            _queueIndex = 0;
        }
        _queue.InsertRange(next ? Math.Min(_queueIndex + 1, _queue.Count) : _queue.Count, items);
        SetNextFromQueue();
        OnPropertyChanged(nameof(NextTip));
    }

    private void SetNextFromQueue()
    {
        if (_queue == null) return;
        _nextEpisode = _queueIndex + 1 < _queue.Count ? _queue[_queueIndex + 1] : null;
        if (_nextEpisode is { } n)
        {
            UpNextTitle = n.IsEpisode ? $"{n.SeriesName}  S{n.ParentIndexNumber}:E{n.IndexNumber}  {n.Name}" : n.Name;
            UpNextImage = n.Tag("Primary") is { } t && n.IsEpisode ? AppServices.Jellyfin.ImageUrl(n.Id, "Primary", t, 420)
                : n.BackdropImageTags is { Count: > 0 } b ? AppServices.Jellyfin.ImageUrl(n.Id, "Backdrop", b[0], 420)
                : n.Tag("Primary") is { } p ? AppServices.Jellyfin.ImageUrl(n.Id, "Primary", p, 420) : null;
        }
        OnPropertyChanged(nameof(HasNext));
    }

    /// <summary>Like a CD player: early in an item goes back one, otherwise restarts this one.</summary>
    [RelayCommand]
    public void PlayPrevious()
    {
        if (IsLive) return;
        if (_queue != null && _queueIndex > 0 && Player.TimeMs < 5000)
        {
            _ = ReportStoppedAsync();
            _queueIndex--;
            _ = StartItemAsync(_queue[_queueIndex], null, null);
            return;
        }
        SeekToMs(0);
    }

    private async Task StartItemAsync(BaseItemDto item, long? startTicks, string? mediaSourceId)
    {
        // Until the new file is handed to mpv, the player is still on the previous one: no
        // reports, or the old file's position would be saved against this item.
        _sourcePending = true;
        _item = item;
        _startTicks = startTicks;
        _segments = new();
        _autoSkipped.Clear();
        _dismissedSegment = null;
        _nextEpisode = null;
        _upNextDismissed = false;
        IsUpNextVisible = false;
        IsSkipVisible = false;
        IsBuffering = true;
        IsSubtitleSearchOpen = false;
        Error = null;
        if (_queue != null) SetNextFromQueue();
        OnPropertyChanged(nameof(HasNext));

        Title = item.IsEpisode ? item.SeriesName ?? item.Name ?? "" : item.Name ?? "";
        Subtitle = item.IsEpisode ? $"S{item.ParentIndexNumber}:E{item.IndexNumber}  {item.Name}" : item.ProductionYear?.ToString();
        var jf = AppServices.Jellyfin;
        LogoUrl = item.Tag("Logo") is { } logo ? jf.ImageUrl(item.Id, "Logo", logo, 500)
            : item.ParentLogoImageTag != null ? jf.ImageUrl(item.ParentLogoItemId, "Logo", item.ParentLogoImageTag, 500) : null;

        try
        {
            // Ask the server for media sources with transcoding explicitly disabled.
            var info = await jf.GetPlaybackInfoAsync(item.Id, startTicks);
            if (_item != item || _disposed) return; // something else was picked meanwhile
            var source = info.MediaSources.FirstOrDefault(s => s.Id == mediaSourceId) ?? info.MediaSources.FirstOrDefault();
            if (source == null) { Error = "The server returned no playable media for this item."; IsBuffering = false; return; }
            _mediaSourceId = source.Id;
            var vs = source.MediaStreams.FirstOrDefault(s => s.Type == "Video");
            _sourceStreams = source.MediaStreams;
            SourceLabel = string.Join(" · ", new[] { VideoFormat.Resolution(vs?.Width), vs?.Codec?.ToUpperInvariant(), VideoFormat.HdrLabel(vs?.VideoRangeType, out _), "Direct play" }
                .Where(x => !string.IsNullOrEmpty(x)));
            _playSessionId = info.PlaySessionId;

            // External subtitle files are fetched as-is (no burn-in, no conversion) and handed to mpv.
            _externalSubs = ExternalSubs(item.Id, source);
            _lastPositionMs = startTicks is > 0 ? TimeSpan.FromTicks(startTicks.Value).TotalMilliseconds : 0;
            SwapMedia(LibraryLoad(item.Id, source, info.PlaySessionId, startTicks is > 0 ? TimeSpan.FromTicks(startTicks.Value).TotalSeconds : null));
            _sourcePending = false;

            await jf.ReportStartAsync(Progress(isPaused: false));
            _lastReport = DateTime.UtcNow;

            _ = LoadTrickplayAsync(item.Id, source.Id!);
            _segments = await AppServices.Segments.GetSegmentsAsync(item.Id);
            if (_queue == null && item.IsEpisode && _settings.AutoPlayNextEpisode) _ = FindNextEpisodeAsync(item);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsBuffering = false;
        }
    }

    /// <summary>An external subtitle file handed to mpv, and the Jellyfin stream index it came from.</summary>
    private sealed record ExternalSub(string Url, string Title, string? Language, int StreamIndex);

    private List<ExternalSub> _externalSubs = new();

    /// <summary>Read-ahead from Settings → Network buffer (mpv's cache-secs).</summary>
    private int BufferSeconds => Math.Clamp(_settings.NetworkBufferSeconds, 2, 300);

    /// <summary>Languages: this device's setting when filled in, otherwise the signed-in user's Jellyfin profile.</summary>
    private string? AudioLang => AppServices.Session.PreferredAudioLanguage(_settings.PreferredAudioLanguage);
    private string? SubLang => AppServices.Session.PreferredSubtitleLanguage(_settings.PreferredSubtitleLanguage);

    /// <summary>
    /// Everything mpv needs for one file from the user's Jellyfin server: the original file, its
    /// external subtitles, language / subtitle preferences, a read-ahead sized from its bitrate,
    /// and the token as a header (never in the URL).
    /// </summary>
    private MpvLoad LibraryLoad(string itemId, MediaSourceInfo source, string? playSessionId, double? startSeconds)
    {
        var jf = AppServices.Jellyfin;
        _sourceStreams = source.MediaStreams;
        var audio = PickAudioStream();
        return new MpvLoad
        {
            Url = jf.DirectStreamUrl(itemId, source.Id, playSessionId),
            StartSeconds = startSeconds,
            AudioLanguage = AudioLang,
            SubtitleLanguage = SubLang,
            SubtitleMode = AppServices.Session.SubtitleMode(_settings.SubtitleMode),
            ExternalSubtitles = ExternalSubs(itemId, source).Select(s => (s.Url, s.Title, s.Language)).ToList(),
            CacheSeconds = BufferSeconds,
            CacheBytes = CacheBytesFor(source),
            AuthHeader = jf.TokenHeaderValue,
            ExclusiveAudio = _settings.AudioPassthrough && PlaybackEngine.NeedsExclusiveAudio(audio?.Codec, audio?.Profile),
        };
    }

    /// <summary>
    /// Memory for read-ahead: enough to really hold "Network buffer" seconds of this file (a 4K
    /// remux needs far more than a 1080p encode), within 64 MiB – 1 GiB.
    /// </summary>
    private long? CacheBytesFor(MediaSourceInfo source)
    {
        long? bps = source.Bitrate;
        if (bps is not > 0 && source.Size is > 0 && source.RunTimeTicks is > 0)
            bps = (long)(source.Size.Value * 8 / TimeSpan.FromTicks(source.RunTimeTicks.Value).TotalSeconds);
        if (bps is not > 0) return null;
        long bytes = (long)(bps.Value / 8.0 * BufferSeconds * 1.5);
        return Math.Clamp(bytes, 64L << 20, 1L << 30);
    }

    private static List<ExternalSub> ExternalSubs(string itemId, MediaSourceInfo source) =>
        source.MediaStreams
            .Where(s => s.Type == "Subtitle" && s.IsExternal)
            .Select(s => new ExternalSub(AppServices.Jellyfin.ExternalSubtitleUrl(itemId, source.Id!, s), s.DisplayTitle ?? s.Title ?? s.Language ?? "External", s.Language, s.Index))
            .ToList();

    private async Task FindNextEpisodeAsync(BaseItemDto ep)
    {
        if (ep.SeriesId == null) return;
        try
        {
            // Only the neighbours of this episode, not the whole series with its media info.
            var near = await AppServices.Jellyfin.GetAdjacentEpisodesAsync(ep.SeriesId, ep.Id);
            var idx = near.Items.FindIndex(e => e.Id == ep.Id);
            if (idx >= 0 && idx + 1 < near.Items.Count && _item?.Id == ep.Id && _queue == null)
            {
                _nextEpisode = near.Items[idx + 1];
                UpNextTitle = $"S{_nextEpisode.ParentIndexNumber}:E{_nextEpisode.IndexNumber}  {_nextEpisode.Name}";
                UpNextImage = _nextEpisode.Tag("Primary") is { } t ? AppServices.Jellyfin.ImageUrl(_nextEpisode.Id, "Primary", t, 420) : null;
                OnPropertyChanged(nameof(HasNext));
            }
        }
        catch { }
    }

    // What the server is told: the last known position (kept even once mpv has shut down) and
    // the audio / subtitle streams actually chosen, so resuming elsewhere picks up the same ones.
    private double _lastPositionMs;
    private int? _audioStreamIndex, _subtitleStreamIndex;
    private DateTime? _reportAfter;
    /// <summary>Set while switching items (e.g. to the next episode) until the new file is loaded.</summary>
    private bool _sourcePending;
    /// <summary>True when the server may be told about <see cref="_item"/> right now.</summary>
    private bool CanReport => _item != null && !_sourcePending && !IsLive;

    private PlaybackProgressInfo Progress(bool isPaused, string? eventName = null)
    {
        var t = Player.TimeMs;
        if (t > 0 && !Player.HasEnded) _lastPositionMs = t;
        return new()
        {
            ItemId = _item?.Id,
            MediaSourceId = _mediaSourceId,
            PlaySessionId = _playSessionId,
            PositionTicks = TimeSpan.FromMilliseconds(Math.Max(0, _lastPositionMs)).Ticks,
            IsPaused = isPaused,
            IsMuted = IsMuted,
            VolumeLevel = Volume,
            PlayMethod = "DirectPlay",
            EventName = eventName,
            AudioStreamIndex = _audioStreamIndex,
            SubtitleStreamIndex = _subtitleStreamIndex,
        };
    }

    /// <summary>Sends a progress report about a second from now (coalesces bursts of seeks).</summary>
    private void ReportSoon() { if (!IsLive && _item != null) _reportAfter = DateTime.UtcNow.AddSeconds(1); }

    private int? StreamIndexOf(MpvTrack t) =>
        t.FfIndex ?? (t.ExternalUrl != null ? _externalSubs.FirstOrDefault(s => s.Url == t.ExternalUrl)?.StreamIndex : null);

    // ===================== Live (NostalgiaTV) =====================

    public static PlayerViewModel ForLive(MainViewModel main, IReadOnlyList<NtvChannel> channels, int index)
    {
        var vm = new PlayerViewModel(main) { IsLive = true };
        vm._channels = channels;
        vm.TuneTo(index);
        return vm;
    }

    // Direct-play live state: the schedule slot currently on screen and the rows after it.
    private List<NtvSlot> _liveSchedule = new();
    private NtvSlot? _liveSlot;
    private int _tuneVersion;
    private bool _switchingSlot;
    private DateTime? _livePausedAt;

    [ObservableProperty] private string? _liveSourceLabel;
    [ObservableProperty] private bool _isLiveDirect;
    [ObservableProperty] private string? _liveInterstitial;

    private void TuneTo(int index)
    {
        if (_channels == null || _channels.Count == 0) return;
        _channelIndex = (index % _channels.Count + _channels.Count) % _channels.Count;
        var ch = _channels[_channelIndex];
        var version = ++_tuneVersion;
        _feedWatchStart = null;
        Error = null;
        LiveInterstitial = null;
        IsBuffering = true;
        Title = ch.Name;
        ChannelNumber = ch.Number;
        LogoUrl = ch.LogoUrl;
        _liveSlot = null;
        _bannerUntil = DateTime.UtcNow.AddSeconds(5);
        IsChannelBannerVisible = true;
        ShowOsd();
        _ = TuneAsync(ch, version);
    }

    private async Task TuneAsync(NtvChannel ch, int version)
    {
        if (_settings.NtvDirectPlay && ch.StableId is { } stableId)
        {
            try
            {
                var schedule = await AppServices.Ntv.GetChannelScheduleAsync(stableId, 6);
                if (version != _tuneVersion) return;
                var now = DateTimeOffset.Now;
                var slot = schedule.FirstOrDefault(s => s.Start <= now && s.End > now);
                if (slot != null && CanDirectPlay(slot))
                {
                    _liveSchedule = schedule;
                    await PlaySlotAsync(slot, version);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Schedule unavailable: fall back to the broadcast feed below.
                Nettle.Core.Services.Log.Warn($"NTV direct tune failed for {stableId}, using feed: {ex.Message}");
            }
            if (version != _tuneVersion) return;
        }

        // Fallback: NostalgiaTV's own broadcast feed (used for channels with no Jellyfin item,
        // e.g. weather or external IPTV, or when direct play is turned off in Settings).
        IsLiveDirect = false;
        _liveSlot = null;
        _sourceStreams = null; // feed: no file metadata, so only verified resolution is badged
        _feedWatchStart = DateTime.UtcNow;
        LiveSourceLabel = "NostalgiaTV broadcast feed";
        // NostalgiaTV's own feed: no Jellyfin token goes to it (AuthHeader stays empty).
        SwapMedia(new MpvLoad { Url = ch.StreamUrl, CacheSeconds = Math.Max(20, BufferSeconds), CacheBytes = 150L << 20 });
        UpdateLiveInfo();
    }

    private static string Norm(string? id) => (id ?? "").Replace("-", "").ToLowerInvariant();

    /// <summary>True when the slot's item lives on the Jellyfin server we're signed into.</summary>
    private static bool CanDirectPlay(NtvSlot slot) =>
        slot.TryGetJellyfinItem(out var serverId, out _) &&
        Norm(serverId) == Norm(AppServices.Session.Current?.ServerId);

    /// <summary>Plays the slot's original file from Jellyfin, joined at the live broadcast position.</summary>
    private async Task PlaySlotAsync(NtvSlot slot, int version)
    {
        var jf = AppServices.Jellyfin;
        // Resolves NostalgiaTV's item id, repairing it via title / episode lookup if it's stale.
        var (itemId, info) = await AppServices.NtvItems.GetPlaybackAsync(slot);
        if (version != _tuneVersion) return;
        // Computed after the lookup so any time spent resolving doesn't put us behind live.
        var offsetMs = Math.Max(0, (DateTimeOffset.Now - slot.Start).TotalMilliseconds) + (slot.PlaybackStartTimeMs ?? 0);
        var source = info.MediaSources.FirstOrDefault();
        if (source?.Id == null) throw new InvalidOperationException("No media source");

        var load = LibraryLoad(itemId, source, info.PlaySessionId, offsetMs > 500 ? offsetMs / 1000.0 : null);

        _liveSlot = slot;
        IsLiveDirect = true;
        LiveInterstitial = null;
        var v = source.MediaStreams.FirstOrDefault(s => s.Type == "Video");
        _sourceStreams = source.MediaStreams;
        var quality = VideoFormat.Resolution(v?.Width) ?? (slot.VideoResolution != null ? slot.VideoResolution + "p" : null);
        LiveSourceLabel = string.Join(" · ", new[] { "Original", quality, v?.Codec?.ToUpperInvariant(), VideoFormat.HdrLabel(v?.VideoRangeType, out _), "Direct play" }.Where(x => !string.IsNullOrEmpty(x)));
        SwapMedia(load);
        UpdateLiveInfo();
    }

    /// <summary>At a slot boundary, roll straight into the next scheduled program.</summary>
    private async Task AdvanceSlotAsync()
    {
        if (_switchingSlot || _liveSlot == null || _channels == null) return;
        _switchingSlot = true;
        Nettle.Core.Services.Log.Info($"slot rollover after '{_liveSlot.DisplayTitle}' ({_liveSlot.End.LocalDateTime:T})");
        var version = _tuneVersion;
        try
        {
            var now = DateTimeOffset.Now;
            var next = _liveSchedule.FirstOrDefault(s => s.Start <= now.AddSeconds(1) && s.End > now)
                       ?? _liveSchedule.FirstOrDefault(s => s.StartTimeMs >= _liveSlot.EndTimeMs - 1000);
            if (next == null || next.End <= now)
            {
                // Ran past the cached rows: refresh the schedule.
                TuneTo(_channelIndex);
                return;
            }
            if (!CanDirectPlay(next)) { TuneTo(_channelIndex); return; }
            await PlaySlotAsync(next, version);
        }
        catch (Exception ex)
        {
            Nettle.Core.Services.Log.Warn("rollover failed, re-tuning: " + ex.Message);
            if (version == _tuneVersion) TuneTo(_channelIndex);
        }
        finally { _switchingSlot = false; }
    }

    private void UpdateLiveInfo()
    {
        if (_channels == null) return;
        var now = DateTimeOffset.Now;
        if (IsLiveDirect && _liveSlot is { } slot)
        {
            var nextSlot = _liveSchedule.FirstOrDefault(s => s.StartTimeMs >= slot.EndTimeMs - 1000);
            NowTitle = slot.DisplayTitle;
            NowTime = $"{slot.Start.LocalDateTime:h:mm} – {slot.End.LocalDateTime:h:mm tt}";
            LiveProgress = Math.Clamp((now - slot.Start) / (slot.End - slot.Start), 0, 1);
            NextTitle = nextSlot != null ? $"Next  {nextSlot.Start.LocalDateTime:h:mm tt}  {nextSlot.DisplayTitle}" : null;
            Subtitle = slot.DisplaySubtitle;
            return;
        }
        var ch = _channels[_channelIndex];
        var p = ch.ProgramAt(now);
        var next = p != null ? ch.ProgramAfter(p.Start) : ch.ProgramAfter(now);
        NowTitle = p?.Title ?? "No listing";
        NowTime = p != null ? $"{p.Start.LocalDateTime:h:mm} – {p.Stop.LocalDateTime:h:mm tt}" : null;
        LiveProgress = p != null ? Math.Clamp((now - p.Start) / p.Duration, 0, 1) : 0;
        NextTitle = next != null ? $"Next  {next.Start.LocalDateTime:h:mm tt}  {next.Title}" : null;
        Subtitle = p?.SubTitle ?? p?.EpisodeNum;
    }

    [RelayCommand] private void ChannelUp() => TuneTo(_channelIndex + 1);
    [RelayCommand] private void ChannelDown() => TuneTo(_channelIndex - 1);

    // ===================== Common =====================

    // mpv's client API is thread-safe and "loadfile replace" atomically swaps files, so fast
    // channel changes and program rollovers need no extra serialization (unlike libVLC).
    private void SwapMedia(MpvLoad load)
    {
        if (_disposed) return;
        _currentUrl = load.Url;
        _mediaGeneration++;
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        _badgeHideCts?.Cancel();
        IsFormatBadgesVisible = false;
        FormatBadges.Clear();
        _chapters = new();
        ChapterMarks = Array.Empty<double>();
        ResetPerFileTuning();
        Nettle.Core.Services.Log.Info($"play {(IsLive ? (IsLiveDirect ? "live-direct" : "live-feed") : "vod")}: {Redact(load.Url)}" +
            (load.AuthHeader != null ? " (token sent as a header)" : ""));
        Player.Load(load);
    }

    /// <summary>Strips the access token from URLs before they reach the log.</summary>
    private static string Redact(string? url) =>
        string.IsNullOrEmpty(url) ? "" : System.Text.RegularExpressions.Regex.Replace(url, "(?i)(ApiKey|api_key)=[^&]*", "$1=***");

    private void OnTick()
    {
        if (_disposed) return;
        IsPlaying = Player.IsPlaying;
        if (Error == null && LiveInterstitial == null) IsBuffering = Player.IsBuffering;

        if (IsOsdVisible && IsPlaying && !IsTracksOpen && !IsSubtitleSearchOpen && (DateTime.UtcNow - _lastInput).TotalSeconds > 3.5)
            IsOsdVisible = false;

        if (IsLive)
        {
            if (IsChannelBannerVisible && DateTime.UtcNow > _bannerUntil) IsChannelBannerVisible = false;
            CheckFeedWatchdog();
            CheckLiveStillWatching();
            if (IsLiveDirect && _liveSlot != null && _livePausedAt == null && DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _liveSlot.EndTimeMs - 250)
                _ = AdvanceSlotAsync();
            UpdateLiveInfo();
            return;
        }

        long len = Player.LengthMs;
        long time = Player.TimeMs;
        if (len > 0) LengthMs = len;
        if (!IsSeeking && time >= 0) SeekValue = time;
        var pos = TimeSpan.FromMilliseconds(Math.Max(0, IsSeeking ? SeekValue : time));
        PositionText = Format.Clock(pos);
        if (len > 0)
        {
            RemainingText = "-" + Format.Clock(TimeSpan.FromMilliseconds(len) - pos);
            EndsAtText = "Ends " + (DateTime.Now + (TimeSpan.FromMilliseconds(len) - pos)).ToString("h:mm tt");
        }

        UpdateSegments(pos, len);

        bool due = (DateTime.UtcNow - _lastReport).TotalSeconds >= 10 || (_reportAfter is { } after && DateTime.UtcNow >= after);
        if (due && CanReport && time > 0 && !Player.IsBuffering)
        {
            _lastReport = DateTime.UtcNow;
            var eventName = _reportAfter != null ? "TimeUpdate" : null;
            _reportAfter = null;
            _ = AppServices.Jellyfin.ReportProgressAsync(Progress(!IsPlaying, eventName));
        }
    }

    private void UpdateSegments(TimeSpan pos, long lenMs)
    {
        PlaybackSegment? active = null;
        foreach (var s in _segments)
        {
            if (pos >= s.Start && pos < s.End - TimeSpan.FromSeconds(0.5)) { active = s; break; }
        }

        if (active != null)
        {
            var action = SegmentService.ActionFor(active.Kind, _settings);
            if (action == SegmentAction.AutoSkip && _autoSkipped.Add(active))
            {
                SeekToMs(active.End.TotalMilliseconds);
                _main.Toast(active.Kind == SegmentKind.Intro ? "Skipped intro" : $"Skipped {active.Kind.ToString().ToLowerInvariant()}");
                active = null;
            }
            else if (action != SegmentAction.ShowButton || active == _dismissedSegment) active = null;
        }

        // Credits with a known next episode turn into the "Up Next" card instead of a plain skip.
        bool creditsNow = active?.Kind == SegmentKind.Credits ||
                          (_segments.All(s => s.Kind != SegmentKind.Credits) && lenMs > 0 && lenMs - pos.TotalMilliseconds < 25_000 && pos.TotalSeconds > 60);
        if (_nextEpisode != null && creditsNow && !_upNextDismissed)
        {
            if (!IsUpNextVisible)
            {
                IsUpNextVisible = true;
                _upNextShownAt = DateTime.UtcNow;
            }
            UpNextCountdown = Math.Max(0, _settings.UpNextCountdownSeconds - (int)(DateTime.UtcNow - _upNextShownAt).TotalSeconds);
            if (UpNextCountdown == 0 && IsPlaying && !IsStillWatchingVisible) { AutoPlayNext(); return; }
            active = null;
        }
        else if (IsUpNextVisible && !creditsNow) IsUpNextVisible = false;

        IsSkipVisible = active != null;
        SkipLabel = active?.SkipLabel;
        _currentSkip = active;
    }

    private PlaybackSegment? _currentSkip;

    [RelayCommand]
    private void Skip()
    {
        if (_currentSkip == null) return;
        SeekToMs(_currentSkip.End.TotalMilliseconds);
        _dismissedSegment = _currentSkip;
        IsSkipVisible = false;
    }

    /// <summary>Next episode chosen by the user (button or N): counts as being here.</summary>
    [RelayCommand]
    private void PlayNext()
    {
        _autoPlayedInARow = 0;
        PlayNextCore();
    }

    private void PlayNextCore()
    {
        if (_nextEpisode == null) return;
        var next = _nextEpisode;
        IsUpNextVisible = false;
        _ = ReportStoppedAsync();
        if (_queue != null && _queueIndex + 1 < _queue.Count) _queueIndex++;
        _ = StartItemAsync(next, null, null);
    }

    [RelayCommand] private void DismissUpNext() { _upNextDismissed = true; IsUpNextVisible = false; }

    // ===================== "Are you still watching?" =====================

    /// <summary>Live TV asks after this long with no input at all.</summary>
    private static readonly TimeSpan LiveStillWatchingAfter =
#if DEBUG
        // Testing: NETTLE_DEV_STILL=<seconds> makes the live prompt appear quickly.
        int.TryParse(Environment.GetEnvironmentVariable("NETTLE_DEV_STILL"), out var devStill) ? TimeSpan.FromSeconds(devStill) :
#endif
        TimeSpan.FromHours(4);

    private int _autoPlayedInARow;
    private DateTime _lastUserInput = DateTime.UtcNow;
    [ObservableProperty] private bool _isStillWatchingVisible;
    [ObservableProperty] private string? _stillWatchingDetail;

    /// <summary>Any key, click or mouse movement from the user.</summary>
    public void UserActivity()
    {
        _lastUserInput = DateTime.UtcNow;
        _autoPlayedInARow = 0;
        ShowOsd();
    }

    /// <summary>Episode ended or the Up Next countdown ran out with nobody touching anything.</summary>
    private void AutoPlayNext()
    {
        if (_nextEpisode == null) return;
        if (_settings.StillWatchingPrompt && _autoPlayedInARow >= Math.Max(1, _settings.StillWatchingEpisodes))
        {
            Player.SetPause(true);
            IsUpNextVisible = false;
            StillWatchingDetail = $"{_autoPlayedInARow + 1} episodes in a row without a break. Nettle paused before the next one.";
            IsStillWatchingVisible = true;
            Nettle.Core.Services.Log.Info("still-watching prompt (episodes)");
            return;
        }
        _autoPlayedInARow++;
        PlayNextCore();
    }

    private void CheckLiveStillWatching()
    {
        if (!_settings.StillWatchingPrompt || IsStillWatchingVisible || !Player.IsPlaying) return;
        if (DateTime.UtcNow - _lastUserInput < LiveStillWatchingAfter) return;
        Player.SetPause(true);
        if (IsLiveDirect) _livePausedAt = DateTime.UtcNow;
        StillWatchingDetail = "This channel has been on for 4 hours without any input, so Nettle paused it.";
        IsStillWatchingVisible = true;
        Nettle.Core.Services.Log.Info("still-watching prompt (live)");
    }

    [RelayCommand]
    private void ContinueWatching()
    {
        IsStillWatchingVisible = false;
        _lastUserInput = DateTime.UtcNow;
        _autoPlayedInARow = 0;
        if (IsLive)
        {
            _livePausedAt = null;
            TuneTo(_channelIndex); // back to the live point, like a real channel
            return;
        }
        if (_nextEpisode != null) PlayNextCore();
        else Player.SetPause(false);
    }

    [RelayCommand]
    private void StopWatching()
    {
        IsStillWatchingVisible = false;
        Close();
    }

    private DateTime? _feedWatchStart;

    /// <summary>
    /// A broadcast feed can "play" while sending no picture (NostalgiaTV couldn't source the
    /// program). If no video frame size has appeared after 15 s, say so instead of a black screen.
    /// </summary>
    private void CheckFeedWatchdog()
    {
        if (_feedWatchStart is not { } started || IsLiveDirect) return;
        if (Player.VideoSize.Width > 0) { _feedWatchStart = null; return; }
        if ((DateTime.UtcNow - started).TotalSeconds < 15 || Error != null) return;
        _feedWatchStart = null;
        IsBuffering = false;
        Error = "NostalgiaTV's broadcast feed isn't sending any video for this channel right now. NostalgiaTV may not be able to find the scheduled file. Try refreshing its library, or pick another channel.";
        Nettle.Core.Services.Log.Warn("feed watchdog: no video after 15s on " + Title);
    }

    private int _liveRetryVersion = -1;
    private static readonly HttpClient ProbeHttp = Nettle.Core.Api.HttpFactory.Create(TimeSpan.FromSeconds(8));

    /// <summary>Requests the first byte of the stream to learn the HTTP status after a failure.</summary>
    private static async Task<int?> ProbeStatusAsync(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            // Same credentials the player used (the URL itself carries no token); only to our own server.
            var jf = AppServices.Jellyfin;
            if (jf.IsServerUrl(url) && jf.TokenHeaderValue is { } auth) req.Headers.TryAddWithoutValidation("Authorization", auth);
            using var resp = await ProbeHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            return (int)resp.StatusCode;
        }
        catch { return null; }
    }

    /// <summary>
    /// Errors are only surfaced if playback is genuinely dead a moment later (a fast channel
    /// change can abandon a stream mid-open), and a live channel gets one silent retry before
    /// any message is shown.
    /// </summary>
    private async Task OnPlaybackErrorAsync(int tuneAtError, string reason)
    {
        Nettle.Core.Services.Log.Warn("playback error from mpv: " + reason);
        await Task.Delay(1200);
        if (_disposed || tuneAtError != _tuneVersion) return; // user already moved on
        if (Player.IsPlaying || (Player.IsBuffering && !Player.HasEnded))
        {
            Nettle.Core.Services.Log.Info("ignored stale playback error (player is healthy)");
            return;
        }

        // Ask the server what went wrong so the message is actionable (and don't retry a missing file).
        var status = await ProbeStatusAsync(_currentUrl);
        if (_disposed || tuneAtError != _tuneVersion) return;
        string what = IsLive ? "this program" : "this item";
        if (status is 404 or 500 or 403)
        {
            IsBuffering = false;
            Error = status switch
            {
                404 => $"Your Jellyfin server can't find the file for {what}. The drive or folder it lives on may be offline, unmounted or moved (server returned 404).",
                403 => $"Your Jellyfin server refused access to {what} (403). Check this user's library permissions.",
                _ => $"Your Jellyfin server hit an error reading the file for {what} (500). The drive may be failing or disconnected.",
            };
            Nettle.Core.Services.Log.Warn($"playback error shown (HTTP {status}): {Error}");
            return;
        }

        if (IsLive && _liveRetryVersion != _tuneVersion)
        {
            Nettle.Core.Services.Log.Warn("live stream error, retrying once");
            TuneTo(_channelIndex);
            _liveRetryVersion = _tuneVersion; // TuneTo bumped the version; remember we retried it
            return;
        }

        IsBuffering = false;
        Error = !IsLive ? "Playback failed. The file may be unreachable from this PC."
            : IsLiveDirect ? "This program couldn't be loaded from your Jellyfin server."
            : "This channel couldn't be played. Check that NostalgiaTV is running.";
        Nettle.Core.Services.Log.Warn("playback error shown: " + Error);
    }

    private void OnEnded()
    {
        if (IsLive)
        {
            if (IsLiveDirect && _liveSlot != null)
            {
                // The file finished a little before its slot did: hold until the next program airs.
                var next = _liveSchedule.FirstOrDefault(s => s.StartTimeMs >= _liveSlot.EndTimeMs - 1000);
                LiveInterstitial = next != null ? $"Coming up at {next.Start.LocalDateTime:h:mm tt}\n{next.DisplayTitle}" : "Coming up next…";
                IsBuffering = false;
                return;
            }
            Error = "The channel stream ended.";
            return;
        }
        if (IsStillWatchingVisible) return;
        // A list the user started (Play all / Shuffle / remote) always moves on; single episodes follow the setting.
        if (_nextEpisode != null && (_settings.AutoPlayNextEpisode || _queue != null) && !_upNextDismissed) { AutoPlayNext(); return; }
        Close();
    }

    [RelayCommand]
    private void TogglePlay()
    {
        ShowOsd();
        if (Player.HasEnded && !IsLive && _item != null) { _ = StartItemAsync(_item, null, _mediaSourceId); return; }
        var willPause = Player.IsPlaying;
        if (IsLive && IsLiveDirect)
        {
            if (willPause) _livePausedAt = DateTime.UtcNow;
            else if (_livePausedAt is { } at)
            {
                _livePausedAt = null;
                // Live TV resumes at the live point, like a real channel.
                if ((DateTime.UtcNow - at).TotalSeconds > 3) { TuneTo(_channelIndex); return; }
            }
        }
        Player.SetPause(willPause);
        IsPlaying = !willPause;
        if (CanReport) _ = AppServices.Jellyfin.ReportProgressAsync(Progress(willPause));
    }

    [RelayCommand] private void SeekBack() => SeekBy(-_settings.SeekStepSeconds);
    [RelayCommand] private void SeekForward() => SeekBy(_settings.SeekStepSeconds);
    public string SeekBackTip => $"Back {_settings.SeekStepSeconds} s (←)";
    public string SeekForwardTip => $"Forward {_settings.SeekStepSeconds} s (→)";

    public void SeekBy(int seconds)
    {
        if (IsLive) return;
        ShowOsd();
        // mpv reports the new position only after a seek lands, so presses in quick succession
        // build on the last target rather than the (stale) reported time: 5 × Right = +5 steps.
        double from = DateTime.UtcNow - _lastSeekAt < TimeSpan.FromSeconds(1.5) ? _lastSeekTargetMs : Math.Max(0, Player.TimeMs);
        var target = Math.Clamp(from + seconds * 1000.0, 0, Math.Max(0, Player.LengthMs - 1000));
        SeekToMs(target);
    }

    private double _lastSeekTargetMs;
    private DateTime _lastSeekAt = DateTime.MinValue;

    public void SeekToMs(double ms)
    {
        if (IsLive) return;
        _lastSeekTargetMs = ms;
        _lastSeekAt = DateTime.UtcNow;
        SeekValue = ms;
        _lastPositionMs = ms;
        Player.SeekTo((long)ms);
        ReportSoon();
    }

    /// <summary>Called by the seek bar when the user releases it.</summary>
    public void CommitSeek()
    {
        IsSeeking = false;
        SeekToMs(SeekValue);
    }

    partial void OnVolumeChanged(int value)
    {
        Player.SetVolume(value);
        _settings.Volume = value;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        Player.SetMute(IsMuted);
    }

    [RelayCommand]
    private void ToggleTracks()
    {
        IsTracksOpen = !IsTracksOpen;
        if (IsTracksOpen) LoadTracks();
        ShowOsd();
    }

    private void LoadTracks()
    {
        var generation = _mediaGeneration;
        _ = Task.Run(Player.GetTracks).ContinueWith(task =>
        {
            if (task.IsFaulted || generation != _mediaGeneration || _disposed) return;
            AudioTracks.Clear();
            SubtitleTracks.Clear();
            var tracks = task.Result;
            foreach (var t in tracks.Where(t => t.Type == "audio"))
                AudioTracks.Add(new TrackOptionViewModel(this, t.Id, t.Name, true, StreamIndexOf(t)) { IsSelected = t.Selected });
            var subs = tracks.Where(t => t.Type == "sub").ToList();
            SubtitleTracks.Add(new TrackOptionViewModel(this, -1, "Off", false, -1) { IsSelected = !subs.Any(s => s.Selected) });
            foreach (var t in subs)
                SubtitleTracks.Add(new TrackOptionViewModel(this, t.Id, t.Name, false, StreamIndexOf(t)) { IsSelected = t.Selected });
            _audioStreamIndex = AudioTracks.FirstOrDefault(a => a.IsSelected)?.StreamIndex;
            _subtitleStreamIndex = SubtitleTracks.FirstOrDefault(s => s.IsSelected)?.StreamIndex;
            Nettle.Core.Services.Log.Info($"tracks: {AudioTracks.Count} audio ({AudioTracks.FirstOrDefault(a => a.IsSelected)?.Name ?? "none selected"}), {subs.Count} subtitle");
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void SelectTrack(TrackOptionViewModel t, bool audio)
    {
        Player.SelectTrack(audio ? "audio" : "sub", t.Id);
        foreach (var o in audio ? AudioTracks : SubtitleTracks) o.IsSelected = o == t;
        if (audio) _audioStreamIndex = t.StreamIndex; else _subtitleStreamIndex = t.StreamIndex;
        // Tell the server right away, so resuming (here or on another device) keeps this choice.
        if (CanReport)
            _ = AppServices.Jellyfin.ReportProgressAsync(Progress(!IsPlaying, audio ? "AudioTrackChange" : "SubtitleTrackChange"));
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        var w = AppServices.Window;
        if (w != null) w.SetFullScreen(!w.IsFullScreen);
    }

    public void ShowOsd()
    {
        _lastInput = DateTime.UtcNow;
        if (!IsOsdVisible) IsOsdVisible = true;
    }

    [RelayCommand]
    public void Close() => _main.ClosePlayer();

    /// <summary>Keyboard / remote handling. Returns true when the key was consumed.</summary>
    public bool HandleKey(Key key)
    {
        _lastUserInput = DateTime.UtcNow;
        _autoPlayedInARow = 0;
        if (IsStillWatchingVisible)
        {
            switch (key)
            {
                case Key.Enter or Key.Space or Key.MediaPlayPause or Key.K: ContinueWatching(); return true;
                case Key.Escape or Key.Back or Key.BrowserBack or Key.MediaStop: StopWatching(); return true;
                default: return true; // the prompt has focus until it's answered
            }
        }
        switch (key)
        {
            case Key.Space or Key.MediaPlayPause or Key.K:
                TogglePlay(); return true;
            case Key.Enter when !IsOsdVisible:
                ShowOsd(); return true;
            case Key.Left or Key.J when IsVod && !IsTracksOpen:
                SeekBy(-_settings.SeekStepSeconds); return true;
            case Key.Right or Key.L when IsVod && !IsTracksOpen:
                SeekBy(_settings.SeekStepSeconds); return true;
            case Key.Up when IsLive && !IsTracksOpen:
            case Key.PageUp when IsLive:
            case Key.MediaNextTrack when IsLive:
                ChannelUp(); return true;
            case Key.Down when IsLive && !IsTracksOpen:
            case Key.PageDown when IsLive:
            case Key.MediaPreviousTrack when IsLive:
                ChannelDown(); return true;
            case Key.Up when IsVod && !IsTracksOpen:
                Volume = Math.Min(125, Volume + 5); ShowOsd(); return true;
            case Key.Down when IsVod && !IsTracksOpen:
                Volume = Math.Max(0, Volume - 5); ShowOsd(); return true;
            case Key.I:
                _ = ShowFormatBadgesAsync(_tuneVersion, _mediaGeneration, delayMs: 0); return true;
            case Key.M:
                ToggleMute(); return true;
            case Key.F or Key.F11:
                ToggleFullScreen(); return true;
            case Key.S:
                if (IsSkipVisible) Skip(); return true;
            case Key.N when HasNext:
            case Key.MediaNextTrack when IsVod && HasNext:
                PlayNext(); return true;
            case Key.MediaPreviousTrack when IsVod:
                PlayPrevious(); return true;
            case Key.PageDown when IsVod:
                NextChapter(); return true;
            case Key.PageUp when IsVod:
                PreviousChapter(); return true;
            case Key.OemOpenBrackets when IsVod:
                SlowerCommand.Execute(null); return true;
            case Key.OemCloseBrackets when IsVod:
                FasterCommand.Execute(null); return true;
            case Key.Z:
                SubtitleEarlierCommand.Execute(null); return true;
            case Key.X:
                SubtitleLaterCommand.Execute(null); return true;
            case Key.Escape or Key.Back or Key.BrowserBack or Key.MediaStop:
                if (IsSubtitleSearchOpen) { IsSubtitleSearchOpen = false; return true; }
                if (IsTracksOpen) { IsTracksOpen = false; return true; }
                Close(); return true;
        }
        ShowOsd();
        return false;
    }

    private async Task ReportStoppedAsync()
    {
        // Closed before the new item's file started: it never began, so there's nothing to stop.
        if (!CanReport) return;
        var p = Progress(isPaused: false);
        p.EventName = "Stopped";
        await AppServices.Jellyfin.ReportStoppedAsync(p);
    }

    /// <summary>Completes once the final "stopped" report (with the resume position) has been sent.</summary>
    public Task Stopped { get; private set; } = Task.CompletedTask;

    // ===================== Trickplay (scrubbing thumbnails) =====================

    private (string ItemId, string SourceId, TrickplayInfo Info)? _trick;
    private readonly Dictionary<int, Avalonia.Media.Imaging.Bitmap?> _trickSheets = new();
    private readonly HashSet<int> _trickLoading = new();
    private readonly List<int> _trickOrder = new();
    private double _trickWantedMs = -1;
    [ObservableProperty] private bool _isTrickVisible;
    [ObservableProperty] private Avalonia.Media.IImage? _trickImage;
    [ObservableProperty] private string _trickTime = "";
    [ObservableProperty] private double _trickWidth = 240;
    [ObservableProperty] private double _trickHeight = 135;
    [ObservableProperty] private bool _hasTrickImage;

    /// <summary>Loads the item's trickplay layout (thumbnails Jellyfin generated), if it has one.</summary>
    private async Task LoadTrickplayAsync(string itemId, string sourceId)
    {
        _trick = null;
        _trickSheets.Clear();
        _trickOrder.Clear();
        _trickLoading.Clear();
        HasTrickImage = false;
        var all = await AppServices.Jellyfin.GetTrickplayAsync(itemId);
        if (_item?.Id != itemId) return;
        var layouts = all == null ? null : all.TryGetValue(sourceId, out var l) ? l : all.Values.FirstOrDefault();
        if (layouts == null || layouts.Count == 0)
        {
            Nettle.Core.Services.Log.Info("trickplay: none for this item (the server hasn't generated scrubbing thumbnails)");
            return;
        }
        // Smallest layout that still looks sharp in the preview (about 320 px wide).
        var info = layouts.Values.Where(i => i.Width >= 280).OrderBy(i => i.Width).FirstOrDefault()
                   ?? layouts.Values.OrderByDescending(i => i.Width).First();
        if (info.Interval <= 0 || info.TileWidth <= 0 || info.TileHeight <= 0 || info.Width <= 0) return;
        _trick = (itemId, sourceId, info);
        TrickWidth = 280;
        TrickHeight = Math.Round(280.0 * info.Height / Math.Max(1, info.Width));
        Nettle.Core.Services.Log.Info($"trickplay: {info.Width}x{info.Height}, every {info.Interval} ms, {info.ThumbnailCount} thumbnails");
    }

    /// <summary>Shows the scrubbing preview for a position (seek bar hover or drag).</summary>
    public void ShowTrickplay(double ms)
    {
        if (IsLive || LengthMs <= 1) return;
        ms = Math.Clamp(ms, 0, LengthMs);
        var chapter = ChapterAt(ms)?.Title;
        TrickTime = Format.Clock(TimeSpan.FromMilliseconds(ms)) + (chapter != null ? "  ·  " + chapter : "");
        IsTrickVisible = true;
        _trickWantedMs = ms;
        if (_trick is not { } t) { HasTrickImage = false; return; }

        var info = t.Info;
        int n = Math.Clamp((int)(ms / info.Interval), 0, Math.Max(0, info.ThumbnailCount - 1));
        int perSheet = info.TileWidth * info.TileHeight;
        int sheet = n / perSheet, k = n % perSheet;
        if (_trickSheets.TryGetValue(sheet, out var bmp))
        {
            if (_trickOrder.Count > 0 && _trickOrder[^1] != sheet) { _trickOrder.Remove(sheet); _trickOrder.Add(sheet); }
            if (bmp == null) { HasTrickImage = false; return; }
            int x = k % info.TileWidth * info.Width, y = k / info.TileWidth * info.Height;
            if (x + info.Width > bmp.PixelSize.Width || y + info.Height > bmp.PixelSize.Height) { HasTrickImage = false; return; }
            TrickImage = new Avalonia.Media.Imaging.CroppedBitmap(bmp, new Avalonia.PixelRect(x, y, info.Width, info.Height));
            HasTrickImage = true;
            return;
        }
        if (_trickLoading.Add(sheet)) _ = LoadTrickSheetAsync(t.ItemId, t.SourceId, info, sheet);
    }

    private async Task LoadTrickSheetAsync(string itemId, string sourceId, TrickplayInfo info, int sheet)
    {
        var url = AppServices.Jellyfin.TrickplayTileUrl(itemId, sourceId, info.Width, sheet);
        var bmp = await AppServices.Images.LoadUncachedAsync(url);
        if (_item?.Id != itemId || _disposed) return;
        // Keep only a few sheets in memory (each holds up to 100 thumbnails).
        // Least recently used first: the sheet around where you're scrubbing stays.
        while (_trickSheets.Count >= 6 && _trickOrder.Count > 0)
        {
            _trickSheets.Remove(_trickOrder[0]);
            _trickOrder.RemoveAt(0);
        }
        _trickSheets[sheet] = bmp;
        _trickOrder.Remove(sheet);
        _trickOrder.Add(sheet);
        _trickLoading.Remove(sheet);
        if (IsTrickVisible && _trickWantedMs >= 0) ShowTrickplay(_trickWantedMs);
    }

    public void HideTrickplay()
    {
        IsTrickVisible = false;
        _trickWantedMs = -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        AppServices.KeepAwake?.SetActive(false);
        if (!IsLive) Stopped = ReportStoppedAsync();
        if (!_wasFullScreen) AppServices.Window?.SetFullScreen(false);
        _ = AppServices.Settings.SaveAsync();
        Nettle.Core.Services.Log.Info("player closed");
        // The video surface already shut mpv down synchronously if it was on screen; this
        // covers a player that never got a surface. Terminate is idempotent.
        Player.Dispose();
    }
}
