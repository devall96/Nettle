using CommunityToolkit.Mvvm.ComponentModel;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.UI.ViewModels;

// Watch together (Jellyfin SyncPlay): this player follows the group. Play / pause / seek / next are asked of the
// group (everyone gets the command), the file starts paused and reports "ready", and a small drift check keeps it
// in step (a slight speed change for small gaps, a jump for big ones).
public sealed partial class PlayerViewModel
{
    private static SyncPlayService Sync => AppServices.SyncPlay;

    /// <summary>The group's queue entry this player shows (null = not playing for a group).</summary>
    private string? _syncEntry;
    public string? SyncEntry => _syncEntry;
    public bool IsSyncPlay => _syncEntry != null && Sync.InGroup;

    [ObservableProperty] private string? _syncPlayLabel;

    private bool _syncWantReady;
    private double _syncReadyTargetMs = -1;
    private bool _syncReportedBuffering;
    private DateTime _syncBufferingSince = DateTime.MaxValue;
    private DateTime _syncNextDriftCheck;
    private DateTime _syncSpeedUntil;
    private bool _syncSpeedActive;
    private DateTime _syncUnpausedAt;
    private CancellationTokenSource? _syncUnpauseCts;

    /// <summary>Plays the group's current item: starts paused at the group's position, then reports ready.</summary>
    public static PlayerViewModel ForSyncPlay(MainViewModel main, BaseItemDto item, long startTicks, string playlistItemId)
    {
        var vm = new PlayerViewModel(main) { _syncEntry = playlistItemId };
        vm._syncWantReady = true;
        vm._syncReadyTargetMs = startTicks / 10_000.0;
        vm.Player.SetSpeed(1); // everyone at normal speed
        vm.UpdateSyncLabel();
        _ = vm.StartItemAsync(item, startTicks, null);
        return vm;
    }

    public void UpdateSyncLabel() =>
        SyncPlayLabel = IsSyncPlay && Sync.Group is { } g ? $"Watching together · {g.Participants.Count}" : null;

    /// <summary>A command for the whole group (from the server).</summary>
    public void ApplySyncCommand(SyncPlayCommand cmd)
    {
        if (!IsSyncPlay || _disposed || (cmd.PlaylistItemId != null && cmd.PlaylistItemId != _syncEntry)) return;
        _syncUnpauseCts?.Cancel();
        var posMs = cmd.PositionTicks / 10_000.0;
        switch (cmd.Command)
        {
            case "Unpause":
            {
                var delay = Sync.ToLocal(cmd.When) - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    SyncSeekIfOff(posMs, paused: true);
                    var cts = _syncUnpauseCts = new CancellationTokenSource();
                    _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
                    {
                        if (t.IsCanceled || _disposed) return;
                        Player.SetPause(false);
                        _syncUnpausedAt = DateTime.UtcNow;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                else
                {
                    // Late (the command took a while to arrive): join where the group already is.
                    SyncSeekIfOff(posMs - delay.TotalMilliseconds);
                    Player.SetPause(false);
                    _syncUnpausedAt = DateTime.UtcNow;
                }
                break;
            }
            case "Pause":
                Player.SetPause(true);
                SyncSeekIfOff(posMs, paused: true);
                break;
            case "Seek":
                Player.SetPause(true);
                SyncSeekIfOff(posMs, always: true);
                _syncWantReady = true; // ready again once the jump has landed
                _syncReadyTargetMs = posMs;
                break;
            case "Stop":
                Close();
                break;
        }
    }

    /// <summary>Moves this player without asking the group (it's the group's own command).</summary>
    private void SyncSeekIfOff(double ms, bool always = false, bool paused = false)
    {
        ms = Math.Max(0, ms);
        // Paused, a seek is cheap and exact: line up to the group's position closely (a quarter second off was
        // noticeable after every pause). Playing, small gaps are left to the drift check.
        if (!always && Math.Abs(Player.TimeMs - ms) < (paused || !Player.IsPlaying ? SyncPlayMath.ToleranceMs / 2 : 300)) return;
        _lastSeekTargetMs = ms;
        _lastSeekAt = DateTime.UtcNow;
        SeekValue = ms;
        _lastPositionMs = ms;
        Player.SeekTo((long)ms);
    }

    /// <summary>From the player's tick: ready / buffering reports and the drift check.</summary>
    private void SyncTick(long timeMs)
    {
        if (!IsSyncPlay || _sourcePending) return;
        var now = DateTime.UtcNow;
        long ticks = (long)(Math.Max(0, timeMs) * 10_000);

        if (_syncWantReady)
        {
            bool landed = _syncReadyTargetMs < 0 || Math.Abs(timeMs - _syncReadyTargetMs) < 1500;
            if (Player.LengthMs > 0 && !Player.IsBuffering && timeMs >= 0 && landed)
            {
                _syncWantReady = false;
                _ = Report(true, ticks, Player.IsPlaying);
            }
            return;
        }

        // Stalled while the group plays: say so (the group waits), and say when it's over.
        if (Player.IsBuffering)
        {
            if (_syncBufferingSince == DateTime.MaxValue) _syncBufferingSince = now;
            if (!_syncReportedBuffering && Sync.State == "Playing" && now - _syncBufferingSince > TimeSpan.FromSeconds(1))
            {
                _syncReportedBuffering = true;
                _ = Report(false, ticks, false);
            }
        }
        else
        {
            _syncBufferingSince = DateTime.MaxValue;
            if (_syncReportedBuffering)
            {
                _syncReportedBuffering = false;
                _ = Report(true, ticks, Player.IsPlaying);
            }
        }

        if (_syncSpeedActive && now >= _syncSpeedUntil)
        {
            _syncSpeedActive = false;
            Player.SetSpeed(1);
        }
        if (now < _syncNextDriftCheck || _syncSpeedActive || !Player.IsPlaying || IsSeeking || now - _syncUnpausedAt < TimeSpan.FromSeconds(3)) return;
        _syncNextDriftCheck = now.AddSeconds(2);
        if (Sync.State != "Playing" || Sync.LastCommand is not { Command: "Unpause" } cmd || (cmd.PlaylistItemId != null && cmd.PlaylistItemId != _syncEntry)) return;
        var expected = SyncPlayMath.ExpectedMs(cmd, Sync.ServerNow);
        var fix = SyncPlayMath.Correction(timeMs, expected);
        switch (fix.Kind)
        {
            case SyncCorrectionKind.Speed:
                Player.SetSpeed(fix.Speed);
                _syncSpeedActive = true;
                _syncSpeedUntil = now.AddMilliseconds(fix.DurationMs);
                Nettle.Core.Services.Log.Info($"SyncPlay: {expected - timeMs:+0;-0} ms from the group, catching up at {fix.Speed:0.00}×");
                break;
            case SyncCorrectionKind.Skip:
                Nettle.Core.Services.Log.Info($"SyncPlay: {expected - timeMs:+0;-0} ms from the group, jumping to it");
                SyncSeekIfOff(fix.TargetMs, always: true);
                _syncUnpausedAt = now; // let the jump land before measuring again
                break;
        }
    }

    private static async Task Report(bool ready, long ticks, bool playing)
    {
        try { await Sync.ReportAsync(ready, ticks, playing); }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn($"SyncPlay {(ready ? "ready" : "buffering")} report failed: {ex.Message}"); }
    }

    /// <summary>Asks the group (pause / play / seek / next): everyone, this player included, follows the command.</summary>
    private void AskGroup(Func<Task> request, string what)
    {
        ShowOsd();
        _ = Task.Run(async () =>
        {
            try { await request(); }
            catch (Exception ex)
            {
                Nettle.Core.Services.Log.Warn($"SyncPlay {what} failed: {ex.Message}");
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Flash("The group didn't answer: " + ex.Message, 3500));
            }
        });
    }
}
