using System.Text.Json;
using Avalonia.Threading;
using Nettle.Core.Api;
using Nettle.Core.Models;
using Nettle.Core.Services;
using Nettle.UI.ViewModels;

namespace Nettle.UI.Services;

/// <summary>
/// Acts on what the server sends over its WebSocket: other Jellyfin apps controlling this one
/// (play, pause, seek, next, volume, tracks, messages), and changes made elsewhere (watched
/// state, new items) so the screen stays current. Also tells the server what Nettle can do,
/// so it appears as a "Play on" target in other apps.
/// </summary>
public sealed class RemoteControl
{
    private readonly JellyfinSocket _socket;
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _refreshQueued;

    private static readonly string[] Commands =
    [
        "DisplayMessage", "SetVolume", "VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute",
        "SetAudioStreamIndex", "SetSubtitleStreamIndex", "ToggleFullscreen", "DisplayContent",
        "GoHome", "GoToSearch", "GoToSettings", "Back", "PlayState", "Play", "PlayNext", "PlayMediaSource",
    ];

    public RemoteControl(JellyfinClient client)
    {
        _socket = new JellyfinSocket(client);
        _socket.Message += (type, data) => Dispatcher.UIThread.Post(() => Handle(type, data));
        _socket.Connected += () => _ = AnnounceAsync(client);
    }

    public void Start() => _socket.Start();
    public void Stop() => _socket.Stop();

    private static async Task AnnounceAsync(JellyfinClient client)
    {
        try
        {
            await client.PostCapabilitiesAsync(new ClientCapabilitiesDto
            {
                PlayableMediaTypes = ["Video"],
                SupportedCommands = Commands.ToList(),
                SupportsMediaControl = true,
            });
        }
        catch (Exception ex) { Log.Warn("couldn't announce remote-control support: " + ex.Message); }
    }

    private static MainViewModel Main => AppServices.Main;
    private static PlayerViewModel? Player => Main.Player;

    private void Handle(string type, JsonElement data)
    {
        if (AppServices.SyncPlay.Handle(type, data)) return; // watch together
        switch (type)
        {
            case "Play": _ = PlayAsync(data); break;
            case "Playstate": Playstate(data); break;
            case "GeneralCommand": General(data); break;
            case "UserDataChanged": RefreshSoon(TimeSpan.FromSeconds(2)); break;
            case "LibraryChanged": RefreshSoon(TimeSpan.FromSeconds(8)); break;
        }
    }

    /// <summary>Refreshes the page on screen once a burst of changes has settled (not while playing).</summary>
    private async void RefreshSoon(TimeSpan settle)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        try
        {
            await Task.Delay(settle);
            if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(5)) await Task.Delay(TimeSpan.FromSeconds(5));
        }
        finally { _refreshQueued = false; }
        if (Player != null) return; // the page refreshes itself when the player closes
        _lastRefresh = DateTime.UtcNow;
        Main.Shell?.RefreshCurrent();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static async Task PlayAsync(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("ItemIds", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array) return;
        var ids = idsEl.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        if (ids.Count == 0) return;
        var command = Str(data, "PlayCommand") ?? "PlayNow";
        var startTicks = Long(data, "StartPositionTicks");
        var startIndex = (int)(Long(data, "StartIndex") ?? 0);
        try
        {
            // One request for all of them; the server returns them in its own order, so put the list back in order.
            var res = await AppServices.Jellyfin.GetItemsAsync(new ItemQuery { Ids = string.Join(",", ids), Recursive = true, SortBy = null, SortOrder = null, Limit = 500 });
            var byId = res.Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            var items = ids.Select(id => byId.GetValueOrDefault(id)).Where(i => i != null && i.IsPlayable).Select(i => i!).ToList();
            if (items.Count == 0) return;
            Log.Info($"remote control: {command} ({items.Count} item(s))");
            if (command is "PlayNext" or "PlayLast" && Player is { IsVod: true } p)
            {
                p.Enqueue(items, next: command == "PlayNext");
                p.Flash(command == "PlayNext" ? "Added to play next" : "Added to the queue");
                return;
            }
            Main.PlayQueue(items, Math.Clamp(startIndex, 0, items.Count - 1), startTicks is > 0 ? startTicks : null);
        }
        catch (Exception ex) { Log.Warn("remote play failed: " + ex.Message); }
    }

    private static void Playstate(JsonElement data)
    {
        var p = Player;
        if (p == null) return;
        switch (Str(data, "Command"))
        {
            case "Stop": p.Close(); break;
            case "Pause": p.RemotePause(true); break;
            case "Unpause": p.RemotePause(false); break;
            case "PlayPause": p.RemotePause(null); break;
            case "NextTrack": p.RemoteNext(); break;
            case "PreviousTrack": p.RemotePrevious(); break;
            case "Seek": if (Long(data, "SeekPositionTicks") is { } t) p.RemoteSeek(t); break;
            case "Rewind": p.SeekBy(-10); break;
            case "FastForward": p.SeekBy(30); break;
        }
    }

    private static void General(JsonElement data)
    {
        var name = Str(data, "Name");
        var args = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Arguments", out var a) ? a : default;
        var p = Player;
        switch (name)
        {
            case "DisplayMessage":
                var text = Str(args, "Text") ?? Str(args, "Header");
                if (string.IsNullOrWhiteSpace(text)) return;
                if (p != null) p.Flash(text, 5000); else Main.Toast(text, 5000);
                break;
            case "SetVolume": if (p != null && Long(args, "Volume") is { } v) p.RemoteSetVolume((int)v); break;
            case "VolumeUp": p?.RemoteVolumeStep(5); break;
            case "VolumeDown": p?.RemoteVolumeStep(-5); break;
            case "Mute": p?.RemoteMute(true); break;
            case "Unmute": p?.RemoteMute(false); break;
            case "ToggleMute": p?.RemoteMute(null); break;
            case "SetAudioStreamIndex": if (p != null && Long(args, "Index") is { } ai) p.SelectByStreamIndex((int)ai, audio: true); break;
            case "SetSubtitleStreamIndex": if (p != null && Long(args, "Index") is { } si) p.SelectByStreamIndex((int)si, audio: false); break;
            case "ToggleFullscreen": p?.ToggleFullScreenCommand.Execute(null); break;
            case "DisplayContent":
                if (p == null && Str(args, "ItemId") is { } itemId) Main.Shell?.Navigate(new DetailsViewModel(itemId));
                break;
            case "GoHome": if (p == null) Main.Shell?.GoHome(); break;
            case "GoToSearch": if (p == null) Main.Shell?.OpenSearchWith(""); break;
            case "GoToSettings": if (p == null) Main.Shell?.NavigateRoot(new SettingsViewModel()); break;
            case "Back": if (p != null) p.Close(); else Main.Shell?.GoBack(); break;
        }
    }
}
