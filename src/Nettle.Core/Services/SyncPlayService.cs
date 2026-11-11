using System.Text.Json;
using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.Core.Services;

/// <summary>
/// SyncPlay ("watch together"): this device's membership of a Jellyfin watch-together group. It follows the group from
/// the server's WebSocket messages (<see cref="Handle"/>), keeps this clock in step with the server's, and tells the
/// app what to play (<see cref="QueueChanged"/>) and when to pause / play / seek (<see cref="CommandReceived"/>).
/// The player asks the group (pause, play, seek, next) instead of acting alone while in a group.
/// Events are raised on whatever thread the message arrived on.
/// </summary>
public sealed class SyncPlayService
{
    private readonly JellyfinClient _jf;
    private CancellationTokenSource? _clockLoop;

    public SyncPlayService(JellyfinClient jf) => _jf = jf;

    /// <summary>The group this device is in (null = not in one).</summary>
    public SyncPlayGroupInfo? Group { get; private set; }
    public bool InGroup => Group != null;
    /// <summary>Idle, Waiting (for everyone to be ready), Paused or Playing.</summary>
    public string State { get; private set; } = "Idle";
    public SyncPlayQueue? Queue { get; private set; }
    public string? CurrentPlaylistItemId => Queue?.Current?.PlaylistItemId;
    /// <summary>The last Unpause / Pause / Seek from the group (what the drift check measures against).</summary>
    public SyncPlayCommand? LastCommand { get; private set; }

    /// <summary>The server's clock minus this one.</summary>
    public TimeSpan Offset { get; private set; }
    public TimeSpan RoundTrip { get; private set; }
    public DateTime ServerNow => DateTime.UtcNow + Offset;
    public DateTime ToLocal(DateTime serverUtc) => serverUtc - Offset;

    public event Action? Changed;
    public event Action<SyncPlayQueue>? QueueChanged;
    public event Action<SyncPlayCommand>? CommandReceived;
    /// <summary>Something to tell the viewer ("Sam joined").</summary>
    public event Action<string>? Notice;

    /// <summary>Handles a WebSocket message if it's a SyncPlay one; true when it was.</summary>
    public bool Handle(string type, JsonElement data)
    {
        switch (type)
        {
            case "SyncPlayCommand":
                if (SyncPlayCommand.From(data) is { } cmd && InGroup)
                {
                    if (cmd.Command is "Unpause" or "Pause" or "Seek") LastCommand = cmd;
                    Log.Info($"SyncPlay: {cmd.Command} at {cmd.PositionTicks / 10_000_000.0:0.0} s");
                    CommandReceived?.Invoke(cmd);
                }
                return true;
            case "SyncPlayGroupUpdate":
                HandleUpdate(data);
                return true;
        }
        return false;
    }

    private void HandleUpdate(JsonElement e)
    {
        var kind = SyncPlayJson.Str(e, "Type");
        var data = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("Data", out var d) ? d : default;
        switch (kind)
        {
            case "GroupJoined":
                Group = SyncPlayGroupInfo.From(data) ?? Group;
                State = Group?.State ?? "Idle";
                Log.Info($"SyncPlay: joined \"{Group?.Name}\"");
                StartClockSync();
                Changed?.Invoke();
                break;
            case "UserJoined":
                if (data.ValueKind == JsonValueKind.String) { AddParticipant(data.GetString()!, true); Notice?.Invoke($"{data.GetString()} is watching with you"); }
                break;
            case "UserLeft":
                if (data.ValueKind == JsonValueKind.String) { AddParticipant(data.GetString()!, false); Notice?.Invoke($"{data.GetString()} left the group"); }
                break;
            case "GroupLeft" or "NotInGroup" or "GroupDoesNotExist":
                if (Group != null && kind != "GroupLeft") Notice?.Invoke("The watch-together group has ended");
                LeftGroup();
                break;
            case "LibraryAccessDenied":
                Notice?.Invoke("Someone in the group can't see this in their libraries, so it can't be played together");
                break;
            case "StateUpdate":
                State = SyncPlayJson.Str(data, "State") ?? State;
                Changed?.Invoke();
                break;
            case "PlayQueue":
                if (SyncPlayQueue.From(data) is { } q)
                {
                    Queue = q;
                    Log.Info($"SyncPlay: queue {q.Reason}, {q.Playlist.Count} item(s), playing #{q.PlayingIndex} from {q.StartPositionTicks / 10_000_000.0:0} s");
                    QueueChanged?.Invoke(q);
                    Changed?.Invoke();
                }
                break;
        }
    }

    private void AddParticipant(string name, bool joined)
    {
        if (Group is not { } g) return;
        var people = g.Participants.ToList();
        if (joined) people.Add(name); else people.Remove(name);
        Group = g with { Participants = people };
        Changed?.Invoke();
    }

    private void LeftGroup()
    {
        if (Group == null && Queue == null) return;
        Group = null;
        Queue = null;
        LastCommand = null;
        State = "Idle";
        _clockLoop?.Cancel();
        _clockLoop = null;
        Log.Info("SyncPlay: left the group");
        Changed?.Invoke();
    }

    // ---------- Joining and leaving ----------

    public Task<List<SyncPlayGroupInfo>> ListAsync(CancellationToken ct = default) => _jf.SyncPlayListAsync(ct);

    /// <summary>Starts a new group with this device in it (the server confirms with GroupJoined).</summary>
    public async Task CreateAsync(string name)
    {
        await SyncClockAsync().ConfigureAwait(false);
        await _jf.SyncPlayNewAsync(name).ConfigureAwait(false);
    }

    public async Task JoinAsync(string groupId)
    {
        await SyncClockAsync().ConfigureAwait(false);
        await _jf.SyncPlayJoinAsync(groupId).ConfigureAwait(false);
    }

    public async Task LeaveAsync()
    {
        try { await _jf.SyncPlayLeaveAsync().ConfigureAwait(false); }
        finally { LeftGroup(); }
    }

    /// <summary>Signing out / switching profile: this device is no longer in the group (the server notices too).</summary>
    public void Reset() => LeftGroup();

    // ---------- Asking the group ----------

    public Task PlayAsync(IReadOnlyList<string> itemIds, int index, long startTicks) => _jf.SyncPlaySetNewQueueAsync(itemIds, index, startTicks);
    public Task PauseAsync() => _jf.SyncPlayPauseAsync();
    public Task UnpauseAsync() => _jf.SyncPlayUnpauseAsync();
    public Task SeekAsync(long ticks) => _jf.SyncPlaySeekAsync(ticks);
    public Task StopAsync() => _jf.SyncPlayStopAsync();
    public Task NextAsync() => CurrentPlaylistItemId is { } id ? _jf.SyncPlayNextItemAsync(id) : Task.CompletedTask;
    public Task PreviousAsync() => CurrentPlaylistItemId is { } id ? _jf.SyncPlayPreviousItemAsync(id) : Task.CompletedTask;

    /// <summary>This player is loaded and waiting (ready) or stalled (not ready), at <paramref name="positionTicks"/>.</summary>
    public Task ReportAsync(bool ready, long positionTicks, bool isPlaying) =>
        CurrentPlaylistItemId is { } id ? _jf.SyncPlayReportAsync(ready, ServerNow, positionTicks, isPlaying, id) : Task.CompletedTask;

    // ---------- Clock ----------

    /// <summary>Measures this clock against the server's (best of a few tries) and tells the server the delay.</summary>
    public async Task SyncClockAsync(int samples = 4)
    {
        TimeSpan bestRtt = TimeSpan.MaxValue, bestOffset = Offset;
        for (int i = 0; i < samples; i++)
        {
            try
            {
                var t0 = DateTime.UtcNow;
                var (t1, t2) = await _jf.GetUtcTimeAsync().ConfigureAwait(false);
                var t3 = DateTime.UtcNow;
                var (offset, rtt) = SyncPlayMath.ClockSample(t0, t1, t2, t3);
                if (rtt < bestRtt) { bestRtt = rtt; bestOffset = offset; }
            }
            catch (Exception ex) { Log.Warn("SyncPlay clock sample failed: " + ex.Message); }
        }
        if (bestRtt == TimeSpan.MaxValue) return;
        Offset = bestOffset;
        RoundTrip = bestRtt;
        Log.Info($"SyncPlay: server clock {Offset.TotalMilliseconds:+0;-0} ms from this one, round trip {RoundTrip.TotalMilliseconds:0} ms");
        if (InGroup) try { await _jf.SyncPlayPingAsync((long)RoundTrip.TotalMilliseconds).ConfigureAwait(false); } catch { }
    }

    /// <summary>While in a group, the clock is measured again every minute (clocks drift, networks change).</summary>
    private void StartClockSync()
    {
        _clockLoop?.Cancel();
        var cts = _clockLoop = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await SyncClockAsync().ConfigureAwait(false);
                try { await Task.Delay(TimeSpan.FromSeconds(60), cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        });
    }
}
