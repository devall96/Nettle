namespace Slopfin.Core.Models;

// NostalgiaTV live channels, read from the server's standard HDHR/IPTV outputs
// (channels.m3u + xmltv.xml). Slopfin intentionally exposes ONLY the live guide,
// never NostalgiaTV's on-demand section.

public sealed class NtvChannel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Number { get; init; }
    public string? LogoUrl { get; set; }
    public string StreamUrl { get; init; } = "";
    public string? Group { get; init; }
    public List<NtvProgram> Programs { get; } = new();

    /// <summary>NostalgiaTV's internal channel id (last segment of /stream/{id}); keys the schedule API.</summary>
    public string? StableId
    {
        get
        {
            var i = StreamUrl.LastIndexOf("/stream/", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var id = StreamUrl[(i + 8)..];
            var q = id.IndexOfAny(new[] { '?', '/' });
            return q >= 0 ? id[..q] : id;
        }
    }

    public NtvProgram? ProgramAt(DateTimeOffset t)
    {
        // Programs are sorted by start time; binary search for the airing one.
        int lo = 0, hi = Programs.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var p = Programs[mid];
            if (t < p.Start) hi = mid - 1;
            else if (t >= p.Stop) lo = mid + 1;
            else return p;
        }
        return null;
    }

    public NtvProgram? ProgramAfter(DateTimeOffset t)
    {
        foreach (var p in Programs)
            if (p.Start > t) return p;
        return null;
    }
}

public sealed class NtvProgram
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset Stop { get; init; }
    public string Title { get; init; } = "";
    public string? SubTitle { get; init; }
    public string? Description { get; init; }
    public string? EpisodeNum { get; init; }
    public string? IconUrl { get; init; }
    public string? Category { get; init; }

    public TimeSpan Duration => Stop - Start;
}

// ---- NostalgiaTV schedule API (/api/schedule?hours=N&channelId=X), camelCase JSON ----
// Each scheduled slot names the media-server item that airs, which lets Slopfin play the
// ORIGINAL file from Jellyfin at the broadcast offset instead of NostalgiaTV's re-encoded feed.

public sealed class NtvScheduleResponse
{
    public long Timestamp { get; set; }
    public List<NtvScheduleChannel> Channels { get; set; } = new();
}

public sealed class NtvScheduleChannel
{
    public string? StableId { get; set; }
    public string? Name { get; set; }
    public List<NtvSlot> Programs { get; set; } = new();
}

public sealed class NtvSlot
{
    public string? Title { get; set; }
    public string? EpisodeTitle { get; set; }
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? Year { get; set; }
    public string? Type { get; set; }
    public long StartTimeMs { get; set; }
    public long EndTimeMs { get; set; }
    public string? Summary { get; set; }
    public string? RatingKey { get; set; }
    public string? ServerId { get; set; }
    public string? VideoResolution { get; set; }
    public string? VideoProfile { get; set; }
    public long? PlaybackStartTimeMs { get; set; }

    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeMilliseconds(StartTimeMs);
    public DateTimeOffset End => DateTimeOffset.FromUnixTimeMilliseconds(EndTimeMs);

    /// <summary>ratingKey "jelly:{serverId}:{itemId}" → the Jellyfin server and item.</summary>
    public bool TryGetJellyfinItem(out string serverId, out string itemId)
    {
        serverId = itemId = "";
        if (RatingKey == null || !RatingKey.StartsWith("jelly:", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = RatingKey.Split(':');
        if (parts.Length < 3 || parts[1].Length == 0 || parts[2].Length == 0) return false;
        serverId = parts[1];
        itemId = parts[2];
        return true;
    }

    public string DisplaySubtitle
    {
        get
        {
            var ep = SeasonNumber != null && EpisodeNumber != null ? $"S{SeasonNumber}:E{EpisodeNumber}" : null;
            return string.Join("  ", new[] { ep, EpisodeTitle }.Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    public string DisplayTitle => ShowTitle ?? Title ?? "";
}
