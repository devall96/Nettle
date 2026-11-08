using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Api;
using Nettle.Core.Models;
using Nettle.UI.Playback;

namespace Nettle.UI.ViewModels;

/// <summary>A subtitle the server found online, offered in the player.</summary>
public sealed partial class RemoteSubtitleViewModel(PlayerViewModel owner, RemoteSubtitleInfo info) : ObservableObject
{
    public RemoteSubtitleInfo Info => info;
    public string Name => info.Name ?? "Subtitle";
    public string Detail => string.Join("  ·  ", new[]
    {
        info.ProviderName,
        info.Format?.ToUpperInvariant(),
        info.IsHashMatch == true ? "exact match for this file" : null,
        info.Forced == true ? "forced" : null,
        info.HearingImpaired == true ? "SDH" : null,
        info.DownloadCount is > 0 ? $"{info.DownloadCount:N0} downloads" : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
    [ObservableProperty] private bool _isDownloading;
    [RelayCommand] private Task Download() => owner.DownloadSubtitleAsync(this);
}

// Chapters, playback fine-tuning (speed, timing, subtitle size), subtitles found by the server,
// and the hooks other Jellyfin apps use to remote-control this player.
public sealed partial class PlayerViewModel
{
    // ===================== Chapters (from the file itself) =====================

    private List<PlayerChapter> _chapters = new();
    /// <summary>Where each chapter starts, as a fraction of the length (for the marks on the seek bar).</summary>
    [ObservableProperty] private IReadOnlyList<double> _chapterMarks = Array.Empty<double>();

    private void LoadChapters()
    {
        var generation = _mediaGeneration;
        _ = Task.Run(Player.GetChapters).ContinueWith(t =>
        {
            if (t.IsFaulted || generation != _mediaGeneration || _disposed) return;
            IEnumerable<PlayerChapter> found = t.Result;
            // Engines that can't read chapters from the file (Media3) use the server's list.
            if (!found.Any() && _item?.Chapters is { Count: > 0 } server)
                found = server.Select(c => new PlayerChapter(c.StartPositionTicks / 10_000_000.0, string.IsNullOrWhiteSpace(c.Name) ? null : c.Name));
            _chapters = found.Where(c => c.Seconds >= 0).OrderBy(c => c.Seconds).ToList();
            UpdateChapterMarks();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    partial void OnLengthMsChanged(double value) => UpdateChapterMarks();

    private void UpdateChapterMarks()
    {
        var len = LengthMs;
        ChapterMarks = len <= 1 || _chapters.Count < 2 || IsLive
            ? Array.Empty<double>()
            : _chapters.Where(c => c.Seconds > 1).Select(c => Math.Clamp(c.Seconds * 1000 / len, 0, 1)).ToList();
    }

    private PlayerChapter? ChapterAt(double ms) => _chapters.LastOrDefault(c => c.Seconds * 1000 <= ms + 1);

    public void NextChapter()
    {
        if (IsLive) return;
        var pos = Math.Max(0, Player.TimeMs);
        var next = _chapters.FirstOrDefault(c => c.Seconds * 1000 > pos + 1000);
        if (next == null) { Flash("No more chapters"); return; }
        SeekToMs(next.Seconds * 1000);
        Flash(next.Title ?? $"Chapter {_chapters.IndexOf(next) + 1}");
    }

    public void PreviousChapter()
    {
        if (IsLive || _chapters.Count == 0) return;
        var pos = Math.Max(0, Player.TimeMs);
        var cur = _chapters.LastOrDefault(c => c.Seconds * 1000 <= pos);
        // Just after a chapter starts, go to the one before (like a disc player).
        var target = cur != null && pos - cur.Seconds * 1000 < 3000 ? _chapters.LastOrDefault(c => c.Seconds < cur.Seconds) ?? cur : cur;
        if (target == null) return;
        SeekToMs(target.Seconds * 1000);
        Flash(target.Title ?? $"Chapter {_chapters.IndexOf(target) + 1}");
    }

    // ===================== Short on-screen messages =====================

    [ObservableProperty] private string? _osdMessage;
    private CancellationTokenSource? _flashCts;

    /// <summary>A brief message over the video (the app's toasts can sit behind it on Windows).</summary>
    public async void Flash(string text, int ms = 1600)
    {
        _flashCts?.Cancel();
        var cts = _flashCts = new CancellationTokenSource();
        OsdMessage = text;
        try { await Task.Delay(ms, cts.Token); } catch (TaskCanceledException) { return; }
        OsdMessage = null;
    }

    // ===================== Fine-tuning: speed, timing, subtitle size =====================

    private static readonly double[] Speeds = [0.5, 0.75, 1, 1.1, 1.25, 1.5, 1.75, 2];

    [ObservableProperty] private double _playbackSpeed = 1;
    [ObservableProperty] private int _subtitleDelayMs;
    [ObservableProperty] private int _audioDelayMs;
    [ObservableProperty] private double _subtitleScale = 1;

    public string SpeedText => PlaybackSpeed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    public string SubtitleDelayText => SignedMs(SubtitleDelayMs);
    public string AudioDelayText => SignedMs(AudioDelayMs);
    public string SubtitleScaleText => $"{SubtitleScale * 100:0}%";
    private static string SignedMs(int ms) => ms == 0 ? "0 ms" : $"{(ms > 0 ? "+" : "−")}{Math.Abs(ms)} ms";

    partial void OnPlaybackSpeedChanged(double value)
    {
        Player.SetSpeed(value);
        _settings.PlaybackSpeed = value;
        OnPropertyChanged(nameof(SpeedText));
    }

    partial void OnSubtitleDelayMsChanged(int value)
    {
        Player.SetSubtitleDelay(value / 1000.0);
        OnPropertyChanged(nameof(SubtitleDelayText));
    }

    partial void OnAudioDelayMsChanged(int value)
    {
        Player.SetAudioDelay(value / 1000.0);
        OnPropertyChanged(nameof(AudioDelayText));
    }

    partial void OnSubtitleScaleChanged(double value)
    {
        Player.SetSubtitleScale(value);
        _settings.SubtitleScale = value;
        OnPropertyChanged(nameof(SubtitleScaleText));
    }

    [RelayCommand]
    private void Slower()
    {
        var s = Speeds.LastOrDefault(x => x < PlaybackSpeed - 0.001, Speeds[0]);
        PlaybackSpeed = s;
        Flash("Speed " + SpeedText);
    }

    [RelayCommand]
    private void Faster()
    {
        var s = Speeds.FirstOrDefault(x => x > PlaybackSpeed + 0.001, Speeds[^1]);
        PlaybackSpeed = s;
        Flash("Speed " + SpeedText);
    }

    [RelayCommand] private void SubtitleEarlier() { SubtitleDelayMs -= 100; Flash("Subtitle timing " + SubtitleDelayText); }
    [RelayCommand] private void SubtitleLater() { SubtitleDelayMs += 100; Flash("Subtitle timing " + SubtitleDelayText); }
    [RelayCommand] private void AudioEarlier() { AudioDelayMs -= 50; Flash("Audio timing " + AudioDelayText); }
    [RelayCommand] private void AudioLater() { AudioDelayMs += 50; Flash("Audio timing " + AudioDelayText); }
    [RelayCommand] private void SmallerSubtitles() => SubtitleScale = Math.Max(0.5, Math.Round(SubtitleScale - 0.1, 2));
    [RelayCommand] private void LargerSubtitles() => SubtitleScale = Math.Min(2.5, Math.Round(SubtitleScale + 0.1, 2));

    [RelayCommand]
    private void ResetTuning()
    {
        PlaybackSpeed = 1;
        SubtitleDelayMs = 0;
        AudioDelayMs = 0;
        SubtitleScale = 1;
        Flash("Back to normal");
    }

    /// <summary>A new file starts with no timing offsets (mpv resets them too).</summary>
    private void ResetPerFileTuning()
    {
        if (SubtitleDelayMs != 0) SubtitleDelayMs = 0;
        if (AudioDelayMs != 0) AudioDelayMs = 0;
    }

    // ===================== Subtitles found by the server =====================

    [ObservableProperty] private bool _isSubtitleSearchOpen;
    [ObservableProperty] private bool _isSubtitleSearching;
    [ObservableProperty] private string? _subtitleSearchStatus;
    [ObservableProperty] private string _subtitleSearchLanguage = "";
    public ObservableCollection<RemoteSubtitleViewModel> RemoteSubtitles { get; } = new();

    /// <summary>Jellyfin wants three-letter codes ("eng"); settings may hold two-letter ones ("en").</summary>
    private static string ThreeLetter(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "eng";
        code = code.Trim();
        if (code.Length == 3) return code.ToLowerInvariant();
        try { return new CultureInfo(code).ThreeLetterISOLanguageName; } catch { return "eng"; }
    }

    [RelayCommand]
    private async Task FindSubtitles()
    {
        if (IsLive || _item == null) return;
        if (string.IsNullOrWhiteSpace(SubtitleSearchLanguage)) SubtitleSearchLanguage = ThreeLetter(SubLang ?? AudioLang);
        IsSubtitleSearchOpen = true;
        IsSubtitleSearching = true;
        SubtitleSearchStatus = null;
        RemoteSubtitles.Clear();
        var item = _item;
        try
        {
            var found = await AppServices.Jellyfin.SearchRemoteSubtitlesAsync(item.Id, ThreeLetter(SubtitleSearchLanguage));
            if (_item != item) return;
            foreach (var s in found.OrderByDescending(s => s.IsHashMatch == true).ThenByDescending(s => s.DownloadCount ?? 0).Take(40))
                RemoteSubtitles.Add(new RemoteSubtitleViewModel(this, s));
            SubtitleSearchStatus = RemoteSubtitles.Count == 0
                ? "Nothing found. Your server needs a subtitle provider plugin (such as Open Subtitles) for this to work."
                : null;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            SubtitleSearchStatus = "Your Jellyfin account isn't allowed to manage subtitles. An administrator can allow it in the user's settings.";
        }
        catch (ApiException ex) { SubtitleSearchStatus = ex.Message; }
        finally { IsSubtitleSearching = false; }
    }

    [RelayCommand] private void CloseSubtitleSearch() => IsSubtitleSearchOpen = false;

    /// <summary>The server downloads the file and stores it with the item; then it's added to what's playing.</summary>
    public async Task DownloadSubtitleAsync(RemoteSubtitleViewModel sub)
    {
        if (_item == null || _mediaSourceId == null || sub.Info.Id == null || sub.IsDownloading) return;
        var item = _item;
        var sourceId = _mediaSourceId;
        sub.IsDownloading = true;
        SubtitleSearchStatus = "Downloading…";
        try
        {
            var known = _externalSubs.Select(s => s.StreamIndex).ToHashSet();
            await AppServices.Jellyfin.DownloadRemoteSubtitleAsync(item.Id, sub.Info.Id);
            // The server saves the file and rescans the item: wait for the new stream to appear.
            for (int attempt = 0; attempt < 8 && _item == item; attempt++)
            {
                await Task.Delay(attempt == 0 ? 600 : 1200);
                var fresh = await AppServices.Jellyfin.GetItemAsync(item.Id);
                var source = fresh.MediaSources?.FirstOrDefault(s => s.Id == sourceId) ?? fresh.MediaSources?.FirstOrDefault();
                var added = source?.MediaStreams.FirstOrDefault(s => s.Type == "Subtitle" && s.IsExternal && !known.Contains(s.Index));
                if (source?.Id == null || added == null) continue;
                var url = AppServices.Jellyfin.ExternalSubtitleUrl(item.Id, source.Id, added);
                var title = added.DisplayTitle ?? sub.Name;
                _externalSubs.Add(new ExternalSub(url, title, added.Language, added.Index));
                Player.AddSubtitle(url, title, added.Language);
                IsSubtitleSearchOpen = false;
                Flash("Subtitles added");
                await Task.Delay(800);
                LoadTracks();
                return;
            }
            SubtitleSearchStatus = "Downloaded, but the server hasn't listed it yet. It will be there next time you play this.";
        }
        catch (ApiException ex) { SubtitleSearchStatus = "Couldn't download: " + ex.Message; }
        finally { sub.IsDownloading = false; }
    }

    // ===================== Remote control (from other Jellyfin apps) =====================

    public bool IsShowingItem(string itemId) => _item?.Id == itemId;

    public void RemotePause(bool? pause)
    {
        bool target = pause ?? Player.IsPlaying;
        if (target == !Player.IsPlaying) return;
        TogglePlay();
    }

    public void RemoteSeek(long ticks) => SeekToMs(TimeSpan.FromTicks(Math.Max(0, ticks)).TotalMilliseconds);

    public void RemoteNext() { if (HasNext) PlayNext(); else if (IsLive) ChannelUp(); }

    public void RemotePrevious() { if (IsLive) ChannelDown(); else PlayPrevious(); }

    public void RemoteSetVolume(int volume) { Volume = Math.Clamp(volume, 0, MaxVolume); ShowOsd(); }

    public void RemoteVolumeStep(int delta) { Volume = Math.Clamp(Volume + delta, 0, MaxVolume); ShowOsd(); }

    public void RemoteMute(bool? mute)
    {
        if (mute == null || mute != IsMuted) ToggleMute();
    }

    /// <summary>Selects by Jellyfin stream index (what other apps send); -1 turns subtitles off.</summary>
    public void SelectByStreamIndex(int index, bool audio)
    {
        var list = audio ? AudioTracks : SubtitleTracks;
        var t = list.FirstOrDefault(x => x.StreamIndex == index) ?? (!audio && index < 0 ? list.FirstOrDefault(x => x.Id < 0) : null);
        if (t != null) SelectTrack(t, audio);
    }
}
