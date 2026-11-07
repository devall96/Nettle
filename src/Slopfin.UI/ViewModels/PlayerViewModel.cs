using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Slopfin.Core.Models;
using Slopfin.Core.Services;
using Slopfin.UI.Playback;

namespace Slopfin.UI.ViewModels;

/// <summary>One TV-style format badge. <see cref="Verified"/> = false means "source has it, but it isn't reaching you".</summary>
public sealed class FormatBadgeViewModel(string label, string detail, bool verified)
{
    public string Label => label;
    public string Detail => detail;
    public bool Verified => verified;
    public bool NotVerified => !verified;
}

public sealed partial class TrackOptionViewModel(PlayerViewModel owner, int id, string name, bool isAudio) : ObservableObject
{
    public int Id => id;
    public string Name => name;
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Select() => owner.SelectTrack(this, isAudio);
}

/// <summary>
/// Player for both library items (direct play of the original file) and NostalgiaTV live
/// channels. Position is polled on a UI timer (cheap, and avoids cross-thread churn from
/// libVLC's event thread).
/// </summary>
public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _tick;
    private readonly PlaybackSettings _settings = AppServices.Settings.Current.Playback;
    private Media? _media;
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

    public MediaPlayer MediaPlayer { get; }

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
    ///  • resolution from the frames libVLC is really decoding,
    ///  • HDR only if the file is HDR AND Windows reports this monitor in HDR mode,
    ///  • premium audio only once an audio track is actually playing (stated as decoded).
    /// </summary>
    private async Task ShowFormatBadgesAsync(int tuneVersion, int generation, int delayMs = 1500)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        uint w = 0, h = 0;
        bool decoded = false;
        for (int i = 0; i < 6 && !decoded; i++)
        {
            if (_disposed || tuneVersion != _tuneVersion || generation != _mediaGeneration) return;
            try { decoded = MediaPlayer.Size(0, ref w, ref h) && w > 0; } catch { }
            if (!decoded) await Task.Delay(500);
        }
        if (!decoded) return; // no picture yet: claim nothing

        var badges = new List<FormatBadgeViewModel>();
        var video = _sourceStreams?.FirstOrDefault(s => s.Type == "Video");

        var res = VideoFormat.Resolution((int)w);
        if (res is "4K" or "1440p" or "1080p")
            badges.Add(new FormatBadgeViewModel(res, $"{w}×{h} decoded", true));

        var hdr = VideoFormat.HdrLabel(video?.VideoRangeType, out var hdrWarning);
        if (hdr != null)
        {
            var display = AppServices.Window?.GetHdrState() ?? HdrDisplayState.Unknown;
            bool isDv = hdr.StartsWith("Dolby Vision");
            bool hlgBase = video?.VideoRangeType is "HLG" or "DOVIWithHLG";
            string signal = hlgBase ? "HLG" : "HDR10";
            if (hdrWarning != null)
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), hdrWarning, false));
            else if (display == HdrDisplayState.Hdr)
                badges.Add(isDv
                    ? new FormatBadgeViewModel("DOLBY VISION", $"Output as {signal} (base layer) to your HDR display", true)
                    : new FormatBadgeViewModel(hdr.ToUpperInvariant(), "HDR output active on this display", true));
            else if (display == HdrDisplayState.Sdr)
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), OperatingSystem.IsWindows()
                    ? "Display is in SDR, so this is tone-mapped. Turn on Windows HDR for true HDR."
                    : "Tone-mapped to SDR (HDR output isn't available on Linux yet)", false));
            else
                badges.Add(new FormatBadgeViewModel(hdr.ToUpperInvariant(), "Couldn't confirm your display's HDR mode", false));
        }

        // Audio: only once libVLC has an audio track running.
        bool audioPlaying = false;
        try { audioPlaying = MediaPlayer.AudioTrack >= 0; } catch { }
        var audio = PickAudioStream();
        var audioLabel = VideoFormat.AudioLabel(audio);
        if (audioPlaying && audioLabel != null)
        {
            var layout = VideoFormat.ChannelLayout(audio!.Channels);
            var detail = audioLabel is "Dolby Atmos" or "DTS:X"
                ? $"Decoded by Slopfin to {layout} (object/height channels need a receiver with passthrough)"
                : $"Decoded by Slopfin · {layout}";
            badges.Add(new FormatBadgeViewModel(audioLabel.ToUpperInvariant(), detail, true));
        }

        if (generation != _mediaGeneration || _disposed) return;
        FormatBadges.Clear();
        foreach (var b in badges) FormatBadges.Add(b);
        Slopfin.Core.Services.Log.Info("format badges: " + (badges.Count == 0 ? "(none)" : string.Join(" | ", badges.Select(b => $"{b.Label} [{b.Detail}]"))));
        if (badges.Count == 0) return;

        IsFormatBadgesVisible = true;
        _badgeHideCts?.Cancel();
        var cts = _badgeHideCts = new CancellationTokenSource();
        try { await Task.Delay(7000, cts.Token); } catch (TaskCanceledException) { return; }
        IsFormatBadgesVisible = false;
    }

    /// <summary>The audio stream libVLC most likely selected (preferred language, else default, else first).</summary>
    private MediaStream? PickAudioStream()
    {
        var audio = _sourceStreams?.Where(s => s.Type == "Audio").ToList();
        if (audio == null || audio.Count == 0) return null;
        var lang = _settings.PreferredAudioLanguage;
        return (!string.IsNullOrEmpty(lang) ? audio.FirstOrDefault(a => string.Equals(a.Language, lang, StringComparison.OrdinalIgnoreCase)) : null)
               ?? audio.FirstOrDefault(a => a.IsDefault) ?? audio[0];
    }
    public ObservableCollection<TrackOptionViewModel> SubtitleTracks { get; } = new();
    public bool HasNext => _nextEpisode != null;
    public bool IsVod => !IsLive;

    private PlayerViewModel(MainViewModel main)
    {
        _main = main;
        MediaPlayer = new MediaPlayer(PlaybackEngine.Vlc)
        {
            EnableHardwareDecoding = _settings.HardwareDecoding,
            EnableKeyInput = false,
            EnableMouseInput = false,
        };
        _volume = Math.Clamp(_settings.Volume, 0, 125);
        MediaPlayer.Volume = _volume;
        MediaPlayer.EndReached += (_, _) => Dispatcher.UIThread.Post(OnEnded);
        MediaPlayer.EncounteredError += (_, _) =>
        {
            var tuneAtError = _tuneVersion;
            Dispatcher.UIThread.Post(() => _ = OnVlcErrorAsync(tuneAtError));
        };
        MediaPlayer.Playing += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            IsBuffering = false;
            LoadTracks();
            _ = ShowFormatBadgesAsync(_tuneVersion, _mediaGeneration);
        });
        MediaPlayer.Buffering += (_, e) =>
        {
            var cache = e.Cache;
            Dispatcher.UIThread.Post(() => IsBuffering = cache < 100f);
        };

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => OnTick());
        _tick.Start();

        _wasFullScreen = AppServices.Window?.IsFullScreen ?? false;
        AppServices.Window?.SetFullScreen(true);
    }

    partial void OnIsLiveChanged(bool value) => OnPropertyChanged(nameof(IsVod));

    // ===================== Library item =====================

    public static PlayerViewModel ForItem(MainViewModel main, BaseItemDto item, long? startTicks, string? mediaSourceId)
    {
        var vm = new PlayerViewModel(main);
        _ = vm.StartItemAsync(item, startTicks, mediaSourceId);
        return vm;
    }

    private async Task StartItemAsync(BaseItemDto item, long? startTicks, string? mediaSourceId)
    {
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
        Error = null;
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
            var source = info.MediaSources.FirstOrDefault(s => s.Id == mediaSourceId) ?? info.MediaSources.FirstOrDefault();
            if (source == null) { Error = "The server returned no playable media for this item."; return; }
            _mediaSourceId = source.Id;
            var vs = source.MediaStreams.FirstOrDefault(s => s.Type == "Video");
            _sourceStreams = source.MediaStreams;
            SourceLabel = string.Join(" · ", new[] { VideoFormat.Resolution(vs?.Width), vs?.Codec?.ToUpperInvariant(), VideoFormat.HdrLabel(vs?.VideoRangeType, out _), "Direct play" }
                .Where(x => !string.IsNullOrEmpty(x)));
            _playSessionId = info.PlaySessionId;

            var url = jf.DirectStreamUrl(item.Id, source.Id, info.PlaySessionId);
            var media = new Media(PlaybackEngine.Vlc, new Uri(url));
            media.AddOption($":network-caching={_settings.NetworkCachingMs}");
            if (startTicks is > 0) media.AddOption($":start-time={TimeSpan.FromTicks(startTicks.Value).TotalSeconds:0.###}".Replace(',', '.'));
            if (!string.IsNullOrEmpty(_settings.PreferredAudioLanguage)) media.AddOption(":audio-language=" + _settings.PreferredAudioLanguage);
            if (!string.IsNullOrEmpty(_settings.PreferredSubtitleLanguage)) media.AddOption(":sub-language=" + _settings.PreferredSubtitleLanguage);

            // External subtitle files are fetched as-is (no burn-in, no conversion) and handed to libVLC.
            foreach (var s in source.MediaStreams.Where(s => s.Type == "Subtitle" && s.IsExternal))
                media.AddSlave(MediaSlaveType.Subtitle, 4, jf.ExternalSubtitleUrl(item.Id, source.Id!, s));

            SwapMedia(media);

            await jf.ReportStartAsync(Progress(isPaused: false));
            _lastReport = DateTime.UtcNow;

            _segments = await AppServices.Segments.GetSegmentsAsync(item.Id);
            if (item.IsEpisode && _settings.AutoPlayNextEpisode) _ = FindNextEpisodeAsync(item);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            IsBuffering = false;
        }
    }

    private async Task FindNextEpisodeAsync(BaseItemDto ep)
    {
        if (ep.SeriesId == null) return;
        try
        {
            var all = await AppServices.Jellyfin.GetEpisodesAsync(ep.SeriesId, null);
            var idx = all.Items.FindIndex(e => e.Id == ep.Id);
            if (idx >= 0 && idx + 1 < all.Items.Count)
            {
                _nextEpisode = all.Items[idx + 1];
                UpNextTitle = $"S{_nextEpisode.ParentIndexNumber}:E{_nextEpisode.IndexNumber}  {_nextEpisode.Name}";
                UpNextImage = _nextEpisode.Tag("Primary") is { } t ? AppServices.Jellyfin.ImageUrl(_nextEpisode.Id, "Primary", t, 420) : null;
                OnPropertyChanged(nameof(HasNext));
            }
        }
        catch { }
    }

    private PlaybackProgressInfo Progress(bool isPaused) => new()
    {
        ItemId = _item?.Id,
        MediaSourceId = _mediaSourceId,
        PlaySessionId = _playSessionId,
        PositionTicks = TimeSpan.FromMilliseconds(Math.Max(0, MediaPlayer.Time)).Ticks,
        IsPaused = isPaused,
        IsMuted = IsMuted,
        VolumeLevel = Volume,
        PlayMethod = "DirectPlay",
    };

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
                Slopfin.Core.Services.Log.Warn($"NTV direct tune failed for {stableId}, using feed: {ex.Message}");
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
        var media = new Media(PlaybackEngine.Vlc, new Uri(ch.StreamUrl));
        media.AddOption($":network-caching={Math.Max(1500, _settings.NetworkCachingMs)}");
        media.AddOption(":live-caching=1500");
        SwapMedia(media);
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

        var media = new Media(PlaybackEngine.Vlc, new Uri(jf.DirectStreamUrl(itemId, source.Id, info.PlaySessionId)));
        media.AddOption($":network-caching={_settings.NetworkCachingMs}");
        if (offsetMs > 500) media.AddOption($":start-time={offsetMs / 1000.0:0.###}".Replace(',', '.'));
        if (!string.IsNullOrEmpty(_settings.PreferredAudioLanguage)) media.AddOption(":audio-language=" + _settings.PreferredAudioLanguage);
        if (!string.IsNullOrEmpty(_settings.PreferredSubtitleLanguage)) media.AddOption(":sub-language=" + _settings.PreferredSubtitleLanguage);
        foreach (var s in source.MediaStreams.Where(s => s.Type == "Subtitle" && s.IsExternal))
            media.AddSlave(MediaSlaveType.Subtitle, 4, jf.ExternalSubtitleUrl(itemId, source.Id, s));

        _liveSlot = slot;
        IsLiveDirect = true;
        LiveInterstitial = null;
        var v = source.MediaStreams.FirstOrDefault(s => s.Type == "Video");
        _sourceStreams = source.MediaStreams;
        var quality = VideoFormat.Resolution(v?.Width) ?? (slot.VideoResolution != null ? slot.VideoResolution + "p" : null);
        LiveSourceLabel = string.Join(" · ", new[] { "Original", quality, v?.Codec?.ToUpperInvariant(), VideoFormat.HdrLabel(v?.VideoRangeType, out _), "Direct play" }.Where(x => !string.IsNullOrEmpty(x)));
        SwapMedia(media);
        UpdateLiveInfo();
    }

    /// <summary>At a slot boundary, roll straight into the next scheduled program.</summary>
    private async Task AdvanceSlotAsync()
    {
        if (_switchingSlot || _liveSlot == null || _channels == null) return;
        _switchingSlot = true;
        Slopfin.Core.Services.Log.Info($"slot rollover after '{_liveSlot.DisplayTitle}' ({_liveSlot.End.LocalDateTime:T})");
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
            Slopfin.Core.Services.Log.Warn("rollover failed, re-tuning: " + ex.Message);
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

    // Every libVLC call that can block (play/stop/seek/pause/dispose) runs on ONE ordered
    // background queue. Running them on independent tasks let a fast channel change, a program
    // rollover or closing the player free a Media while another call was still using it, which
    // crashes natively. Serializing them makes those races impossible and keeps the UI thread free.
    private readonly object _vlcGate = new();
    private Task _vlcChain = Task.CompletedTask;

    private void Vlc(string what, Action action)
    {
        lock (_vlcGate)
        {
            _vlcChain = _vlcChain.ContinueWith(_ =>
            {
                try { action(); }
                catch (Exception ex) { Slopfin.Core.Services.Log.Error($"vlc call '{what}' failed", ex); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void SwapMedia(Media media)
    {
        var old = _media;
        _media = media;
        _mediaGeneration++;
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        _badgeHideCts?.Cancel();
        IsFormatBadgesVisible = false;
        FormatBadges.Clear();
        Slopfin.Core.Services.Log.Info($"play {(IsLive ? (IsLiveDirect ? "live-direct" : "live-feed") : "vod")}: {Redact(media.Mrl)}");
        Vlc("play", () =>
        {
            if (_disposed) { media.Dispose(); return; }
            MediaPlayer.Play(media);
            old?.Dispose(); // safe: the player has already switched to the new media
        });
    }

    /// <summary>Strips the access token from URLs before they reach the log.</summary>
    private static string Redact(string? url) =>
        string.IsNullOrEmpty(url) ? "" : System.Text.RegularExpressions.Regex.Replace(url, "(?i)(ApiKey|api_key)=[^&]*", "$1=***");

    private void OnTick()
    {
        if (_disposed) return;
        IsPlaying = MediaPlayer.IsPlaying;

        if (IsOsdVisible && IsPlaying && !IsTracksOpen && (DateTime.UtcNow - _lastInput).TotalSeconds > 3.5)
            IsOsdVisible = false;

        if (IsLive)
        {
            if (IsChannelBannerVisible && DateTime.UtcNow > _bannerUntil) IsChannelBannerVisible = false;
            CheckFeedWatchdog();
            if (IsLiveDirect && _liveSlot != null && _livePausedAt == null && DateTimeOffset.Now.ToUnixTimeMilliseconds() >= _liveSlot.EndTimeMs - 250)
                _ = AdvanceSlotAsync();
            UpdateLiveInfo();
            return;
        }

        long len = MediaPlayer.Length;
        long time = MediaPlayer.Time;
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

        if ((DateTime.UtcNow - _lastReport).TotalSeconds >= 10 && _item != null && time > 0)
        {
            _lastReport = DateTime.UtcNow;
            _ = AppServices.Jellyfin.ReportProgressAsync(Progress(!IsPlaying));
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
            if (UpNextCountdown == 0 && IsPlaying) { PlayNext(); return; }
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

    [RelayCommand]
    private void PlayNext()
    {
        if (_nextEpisode == null) return;
        var next = _nextEpisode;
        IsUpNextVisible = false;
        _ = ReportStoppedAsync();
        _ = StartItemAsync(next, null, null);
    }

    [RelayCommand] private void DismissUpNext() { _upNextDismissed = true; IsUpNextVisible = false; }

    private DateTime? _feedWatchStart;

    /// <summary>
    /// A broadcast feed can "play" while sending no picture (NostalgiaTV couldn't source the
    /// program). If no video frame size has appeared after 15 s, say so instead of a black screen.
    /// </summary>
    private void CheckFeedWatchdog()
    {
        if (_feedWatchStart is not { } started || IsLiveDirect) return;
        uint w = 0, h = 0;
        bool hasVideo = false;
        try { hasVideo = MediaPlayer.Size(0, ref w, ref h) && w > 0; } catch { }
        if (hasVideo) { _feedWatchStart = null; return; }
        if ((DateTime.UtcNow - started).TotalSeconds < 15 || Error != null) return;
        _feedWatchStart = null;
        IsBuffering = false;
        Error = "NostalgiaTV's broadcast feed isn't sending any video for this channel right now. NostalgiaTV may not be able to find the scheduled file. Try refreshing its library, or pick another channel.";
        Slopfin.Core.Services.Log.Warn("feed watchdog: no video after 15s on " + Title);
    }

    private int _liveRetryVersion = -1;
    private static readonly HttpClient ProbeHttp = Slopfin.Core.Api.HttpFactory.Create(TimeSpan.FromSeconds(8));

    /// <summary>Requests the first byte of the stream to learn the HTTP status after a failure.</summary>
    private static async Task<int?> ProbeStatusAsync(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var resp = await ProbeHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            return (int)resp.StatusCode;
        }
        catch { return null; }
    }

    /// <summary>
    /// libVLC raises an error for a stream that was abandoned mid-open (fast channel changes),
    /// so errors are only surfaced if playback is genuinely dead a moment later, and a live
    /// channel gets one silent retry before any message is shown.
    /// </summary>
    private async Task OnVlcErrorAsync(int tuneAtError)
    {
        await Task.Delay(1200);
        if (_disposed || tuneAtError != _tuneVersion) return; // user already moved on
        var state = MediaPlayer.State;
        if (state is VLCState.Playing or VLCState.Opening or VLCState.Buffering or VLCState.Paused)
        {
            Slopfin.Core.Services.Log.Info("ignored stale vlc error (player is healthy)");
            return;
        }

        // Ask the server what went wrong so the message is actionable (and don't retry a missing file).
        var status = await ProbeStatusAsync(_media?.Mrl);
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
            Slopfin.Core.Services.Log.Warn($"playback error shown (HTTP {status}): {Error}");
            return;
        }

        if (IsLive && _liveRetryVersion != _tuneVersion)
        {
            Slopfin.Core.Services.Log.Warn("live stream error, retrying once");
            TuneTo(_channelIndex);
            _liveRetryVersion = _tuneVersion; // TuneTo bumped the version; remember we retried it
            return;
        }

        IsBuffering = false;
        Error = !IsLive ? "Playback failed. The file may be unreachable from this PC."
            : IsLiveDirect ? "This program couldn't be loaded from your Jellyfin server."
            : "This channel couldn't be played. Check that NostalgiaTV is running.";
        Slopfin.Core.Services.Log.Warn("playback error shown: " + Error);
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
        if (_nextEpisode != null && _settings.AutoPlayNextEpisode && !_upNextDismissed) { PlayNext(); return; }
        Close();
    }

    [RelayCommand]
    private void TogglePlay()
    {
        ShowOsd();
        if (MediaPlayer.State == VLCState.Ended && !IsLive) { SeekToMs(0); }
        var willPause = MediaPlayer.IsPlaying;
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
        Vlc("pause", () => MediaPlayer.SetPause(willPause));
        IsPlaying = !willPause;
        if (!IsLive) _ = AppServices.Jellyfin.ReportProgressAsync(Progress(willPause));
    }

    [RelayCommand] private void SeekBack() => SeekBy(-_settings.SeekStepSeconds);
    [RelayCommand] private void SeekForward() => SeekBy(_settings.SeekStepSeconds * 3);

    public void SeekBy(int seconds)
    {
        if (IsLive) return;
        ShowOsd();
        var target = Math.Clamp(MediaPlayer.Time + seconds * 1000L, 0, Math.Max(0, MediaPlayer.Length - 1000));
        SeekToMs(target);
    }

    public void SeekToMs(double ms)
    {
        if (IsLive) return;
        SeekValue = ms;
        var t = (long)ms;
        Vlc("seek", () => MediaPlayer.Time = t);
    }

    /// <summary>Called by the seek bar when the user releases it.</summary>
    public void CommitSeek()
    {
        IsSeeking = false;
        SeekToMs(SeekValue);
    }

    partial void OnVolumeChanged(int value)
    {
        MediaPlayer.Volume = value;
        _settings.Volume = value;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        MediaPlayer.Mute = IsMuted;
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
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        try
        {
            int curA = MediaPlayer.AudioTrack, curS = MediaPlayer.Spu;
            foreach (var t in MediaPlayer.AudioTrackDescription)
                if (t.Id >= 0) AudioTracks.Add(new TrackOptionViewModel(this, t.Id, t.Name ?? $"Track {t.Id}", true) { IsSelected = t.Id == curA });
            foreach (var t in MediaPlayer.SpuDescription)
                SubtitleTracks.Add(new TrackOptionViewModel(this, t.Id, t.Id < 0 ? "Off" : t.Name ?? $"Track {t.Id}", false) { IsSelected = t.Id == curS });
        }
        catch { }
    }

    public void SelectTrack(TrackOptionViewModel t, bool audio)
    {
        if (audio) { MediaPlayer.SetAudioTrack(t.Id); foreach (var a in AudioTracks) a.IsSelected = a == t; }
        else { MediaPlayer.SetSpu(t.Id); foreach (var s in SubtitleTracks) s.IsSelected = s == t; }
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
                PlayNext(); return true;
            case Key.Escape or Key.Back or Key.BrowserBack or Key.MediaStop:
                if (IsTracksOpen) { IsTracksOpen = false; return true; }
                Close(); return true;
        }
        ShowOsd();
        return false;
    }

    private async Task ReportStoppedAsync()
    {
        if (_item == null) return;
        var p = Progress(isPaused: false);
        p.EventName = "Stopped";
        await AppServices.Jellyfin.ReportStoppedAsync(p);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tick.Stop();
        if (!IsLive) _ = ReportStoppedAsync();
        if (!_wasFullScreen) AppServices.Window?.SetFullScreen(false);
        _ = AppServices.Settings.SaveAsync();
        var mp = MediaPlayer;
        var media = _media;
        _media = null;
        Slopfin.Core.Services.Log.Info("player closed");
        // Queued after any pending play/seek, so nothing can touch the player once it's freed.
        Vlc("dispose", () =>
        {
            try { mp.Stop(); } catch { }
            mp.Dispose();
            media?.Dispose();
        });
    }
}
