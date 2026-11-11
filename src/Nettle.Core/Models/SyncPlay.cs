using System.Globalization;
using System.Text.Json;

namespace Nettle.Core.Models;

// SyncPlay ("watch together"): Jellyfin keeps a group of clients on the same item at the same moment. These are the
// pieces Nettle reads from the server's /SyncPlay endpoints and its "SyncPlayGroupUpdate" / "SyncPlayCommand"
// WebSocket messages (parsed by hand from JSON, so nothing needs registering for the source-generated serializers).

/// <summary>A watch-together group as the server lists it.</summary>
public sealed record SyncPlayGroupInfo(string GroupId, string Name, string State, IReadOnlyList<string> Participants)
{
    public static SyncPlayGroupInfo? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var id = SyncPlayJson.Str(e, "GroupId");
        if (string.IsNullOrEmpty(id)) return null;
        var people = e.TryGetProperty("Participants", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : new List<string>();
        return new SyncPlayGroupInfo(id, SyncPlayJson.Str(e, "GroupName") ?? "Group", SyncPlayJson.Str(e, "State") ?? "Idle", people);
    }
}

/// <summary>One entry of the group's play queue (the same item can be queued twice, so entries have their own id).</summary>
public sealed record SyncPlayQueueItem(string ItemId, string PlaylistItemId);

/// <summary>The group's play queue: what's on, and from where. Sent whenever it changes.</summary>
public sealed record SyncPlayQueue(string Reason, IReadOnlyList<SyncPlayQueueItem> Playlist, int PlayingIndex, long StartPositionTicks, bool IsPlaying)
{
    public SyncPlayQueueItem? Current => PlayingIndex >= 0 && PlayingIndex < Playlist.Count ? Playlist[PlayingIndex] : null;

    public static SyncPlayQueue? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var list = new List<SyncPlayQueueItem>();
        if (e.TryGetProperty("Playlist", out var pl) && pl.ValueKind == JsonValueKind.Array)
            foreach (var x in pl.EnumerateArray())
                if (SyncPlayJson.Str(x, "ItemId") is { } item && SyncPlayJson.Str(x, "PlaylistItemId") is { } entry)
                    list.Add(new SyncPlayQueueItem(item, entry));
        return new SyncPlayQueue(SyncPlayJson.Str(e, "Reason") ?? "", list, (int)(SyncPlayJson.Long(e, "PlayingItemIndex") ?? -1),
            SyncPlayJson.Long(e, "StartPositionTicks") ?? 0, SyncPlayJson.Bool(e, "IsPlaying") ?? false);
    }
}

/// <summary>
/// A command for everyone in the group: Unpause / Pause / Seek / Stop, to happen at <see cref="When"/> (the server's
/// clock, UTC) with the video at <see cref="PositionTicks"/>.
/// </summary>
public sealed record SyncPlayCommand(string Command, DateTime When, long PositionTicks, string? PlaylistItemId)
{
    public static SyncPlayCommand? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || SyncPlayJson.Str(e, "Command") is not { } cmd) return null;
        return new SyncPlayCommand(cmd, SyncPlayJson.Time(e, "When") ?? DateTime.UtcNow, SyncPlayJson.Long(e, "PositionTicks") ?? 0, SyncPlayJson.Str(e, "PlaylistItemId"));
    }
}

public static class SyncPlayJson
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    public static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static DateTime? Time(JsonElement e, string name) => ParseTime(Str(e, name));

    public static DateTime? ParseTime(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    /// <summary>How the server wants times: ISO 8601, UTC.</summary>
    public static string Format(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}

/// <summary>What to do about the difference between where the video is and where the group is.</summary>
public enum SyncCorrectionKind { None, Speed, Skip }

public readonly record struct SyncCorrection(SyncCorrectionKind Kind, double Speed = 1, double DurationMs = 0, double TargetMs = 0);

/// <summary>The arithmetic of staying together, kept free of any player so it can be tested.</summary>
public static class SyncPlayMath
{
    /// <summary>Closer than this counts as together.</summary>
    public const double ToleranceMs = 120;
    /// <summary>Further apart than this, jump instead of catching up gently.</summary>
    public const double SkipAboveMs = 2000;
    /// <summary>Catching up by speed takes about this long, within the speed limits below.</summary>
    public const double CatchUpOverMs = 4000;
    public const double MaxSpeedChange = 0.12;

    /// <summary>
    /// The server clock minus this one, and the round trip, from one /GetUtcTime exchange (the NTP formula):
    /// t0 sent here, t1 received there, t2 answered there, t3 back here.
    /// </summary>
    public static (TimeSpan Offset, TimeSpan RoundTrip) ClockSample(DateTime t0, DateTime t1, DateTime t2, DateTime t3) =>
        (((t1 - t0) + (t2 - t3)) / 2, (t3 - t0) - (t2 - t1));

    /// <summary>Where the group's video is at <paramref name="serverNow"/>, after an Unpause command.</summary>
    public static double ExpectedMs(SyncPlayCommand unpause, DateTime serverNow) =>
        unpause.PositionTicks / 10_000.0 + Math.Max(0, (serverNow - unpause.When).TotalMilliseconds);

    /// <summary>How to get from <paramref name="actualMs"/> to <paramref name="expectedMs"/>.</summary>
    public static SyncCorrection Correction(double actualMs, double expectedMs)
    {
        var diff = expectedMs - actualMs; // + = this player is behind the group
        if (Math.Abs(diff) < ToleranceMs) return new(SyncCorrectionKind.None);
        if (Math.Abs(diff) >= SkipAboveMs) return new(SyncCorrectionKind.Skip, TargetMs: expectedMs);
        var change = Math.Clamp(diff / CatchUpOverMs, -MaxSpeedChange, MaxSpeedChange);
        return new(SyncCorrectionKind.Speed, Speed: 1 + change, DurationMs: Math.Abs(diff / change));
    }
}
