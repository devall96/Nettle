using Avalonia.Threading;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.UI.Services;

/// <summary>Connects the SyncPlay group to the app: opens what the group plays, passes commands to the player.</summary>
public static class WatchTogether
{
    private static int _queueVersion;

    public static void Attach(SyncPlayService sync)
    {
        sync.QueueChanged += q => Dispatcher.UIThread.Post(() => _ = OnQueueAsync(sync, q));
        sync.CommandReceived += c => Dispatcher.UIThread.Post(() => AppServices.Main?.Player?.ApplySyncCommand(c));
        sync.Notice += text => Dispatcher.UIThread.Post(() =>
        {
            if (AppServices.Main?.Player is { } p) p.Flash(text, 3500); else AppServices.Main?.Toast(text, 3500);
        });
        sync.Changed += () => Dispatcher.UIThread.Post(() => AppServices.Main?.Player?.UpdateSyncLabel());
    }

    /// <summary>The group's queue changed: open its current item here (unless it's already what's on screen).</summary>
    private static async Task OnQueueAsync(SyncPlayService sync, SyncPlayQueue q)
    {
        if (q.Current is not { } current || AppServices.Main is not { } main) return;
        if (main.Player?.SyncEntry == current.PlaylistItemId) return; // already showing it (e.g. something was queued)
        var version = ++_queueVersion;
        try
        {
            var item = await AppServices.Jellyfin.GetItemAsync(current.ItemId);
            if (version != _queueVersion || sync.Queue?.Current?.PlaylistItemId != current.PlaylistItemId) return; // changed meanwhile
            main.OpenSyncPlay(item, q.StartPositionTicks, current.PlaylistItemId);
        }
        catch (Exception ex)
        {
            Log.Warn("SyncPlay: couldn't open the group's item: " + ex.Message);
            main.Toast("The group is playing something this profile can't open: " + ex.Message, 5000);
        }
    }
}
