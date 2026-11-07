namespace Nettle.Core.Models;

// Minimal subset of the Jellyfin REST API DTOs that Nettle needs.
// Property names follow the server's PascalCase JSON; deserialization is case-insensitive.

public sealed class PublicSystemInfo
{
    public string? LocalAddress { get; set; }
    public string? ServerName { get; set; }
    public string? Version { get; set; }
    public string? ProductName { get; set; }
    public string? Id { get; set; }
    public bool StartupWizardCompleted { get; set; }
}

public sealed class UserPolicy
{
    public bool IsAdministrator { get; set; }
}

public sealed class UserDto
{
    public string? Name { get; set; }
    public string? ServerId { get; set; }
    public string Id { get; set; } = "";
    public string? PrimaryImageTag { get; set; }
    public bool HasPassword { get; set; }
    public bool HasConfiguredPassword { get; set; }
    public UserPolicy? Policy { get; set; }
}

public sealed class AuthenticateUserByName
{
    public string? Username { get; set; }
    public string? Pw { get; set; }
}

public sealed class AuthenticationResult
{
    public UserDto? User { get; set; }
    public string? AccessToken { get; set; }
    public string? ServerId { get; set; }
}

public sealed class QuickConnectResult
{
    public bool Authenticated { get; set; }
    public string? Secret { get; set; }
    public string? Code { get; set; }
}

public sealed class QuickConnectDto
{
    public string? Secret { get; set; }
}

public sealed class QueryResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalRecordCount { get; set; }
    public int StartIndex { get; set; }
}

public sealed class UserItemDataDto
{
    public double? PlayedPercentage { get; set; }
    public long PlaybackPositionTicks { get; set; }
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public int? UnplayedItemCount { get; set; }
}

public sealed class BaseItemPerson
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Role { get; set; }
    public string? Type { get; set; }
    public string? PrimaryImageTag { get; set; }
}

public sealed class NameGuidPair
{
    public string? Name { get; set; }
    public string? Id { get; set; }
}

public sealed class ChapterInfo
{
    public long StartPositionTicks { get; set; }
    public string? Name { get; set; }
    public string? ImageTag { get; set; }
}

public sealed class MediaStream
{
    public int Index { get; set; }
    public string? Type { get; set; }           // Video | Audio | Subtitle | EmbeddedImage
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public string? DisplayTitle { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsExternal { get; set; }
    public string? DeliveryUrl { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoRange { get; set; }
    public string? VideoRangeType { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public int? BitRate { get; set; }
}

public sealed class MediaSourceInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Protocol { get; set; }
    public string? Container { get; set; }
    public long? Size { get; set; }
    public int? Bitrate { get; set; }
    public long? RunTimeTicks { get; set; }
    public bool SupportsDirectPlay { get; set; }
    public bool IsRemote { get; set; }
    public string? LiveStreamId { get; set; }
    public int? DefaultAudioStreamIndex { get; set; }
    public int? DefaultSubtitleStreamIndex { get; set; }
    public List<MediaStream> MediaStreams { get; set; } = new();
}

public sealed class BaseItemDto
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? OriginalTitle { get; set; }
    public string? ServerId { get; set; }
    public string? Type { get; set; }
    public string? MediaType { get; set; }
    public string? CollectionType { get; set; }
    public bool IsFolder { get; set; }
    public string? Overview { get; set; }
    public List<string>? Taglines { get; set; }
    public List<string>? Genres { get; set; }
    public List<NameGuidPair>? Studios { get; set; }
    public List<BaseItemPerson>? People { get; set; }
    public int? ProductionYear { get; set; }
    public DateTimeOffset? PremiereDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public string? Status { get; set; }
    public string? OfficialRating { get; set; }
    public float? CommunityRating { get; set; }
    public float? CriticRating { get; set; }
    public long? RunTimeTicks { get; set; }
    public string? ParentId { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public string? SeasonId { get; set; }
    public string? SeasonName { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public int? ChildCount { get; set; }
    public double? PrimaryImageAspectRatio { get; set; }
    public Dictionary<string, string>? ImageTags { get; set; }
    public List<string>? BackdropImageTags { get; set; }
    public string? ParentBackdropItemId { get; set; }
    public List<string>? ParentBackdropImageTags { get; set; }
    public string? ParentLogoItemId { get; set; }
    public string? ParentLogoImageTag { get; set; }
    public string? ParentThumbItemId { get; set; }
    public string? ParentThumbImageTag { get; set; }
    public string? SeriesPrimaryImageTag { get; set; }
    public UserItemDataDto? UserData { get; set; }
    public List<MediaSourceInfo>? MediaSources { get; set; }
    public List<MediaStream>? MediaStreams { get; set; }
    public List<ChapterInfo>? Chapters { get; set; }
    /// <summary>Scrubbing thumbnails: media source id → thumbnail width → tile sheet layout.</summary>
    public Dictionary<string, Dictionary<string, TrickplayInfo>>? Trickplay { get; set; }

    public bool IsSeries => Type == "Series";
    public bool IsEpisode => Type == "Episode";
    public bool IsSeason => Type == "Season";
    public bool IsMovie => Type == "Movie";
    public bool IsPlayable => Type is "Movie" or "Episode" or "Video" or "MusicVideo" or "Audio" or "TvChannel";

    public string? Tag(string imageType) =>
        ImageTags != null && ImageTags.TryGetValue(imageType, out var t) ? t : null;
}

/// <summary>
/// Jellyfin 10.9+ trickplay: thumbnails every <see cref="Interval"/> ms, packed into JPEG
/// sheets of <see cref="TileWidth"/> × <see cref="TileHeight"/> thumbnails each.
/// </summary>
public sealed class TrickplayInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int ThumbnailCount { get; set; }
    public int Interval { get; set; }
    public int Bandwidth { get; set; }
}

public sealed class DirectPlayProfile
{
    public string Type { get; set; } = "Video";
}

public sealed class SubtitleProfile
{
    public string Format { get; set; } = "";
    public string Method { get; set; } = "External";
}

/// <summary>
/// A deliberately permissive profile: Nettle uses mpv, which can decode practically anything,
/// so we tell the server "direct play everything" and never offer any transcoding profile.
/// </summary>
public sealed class DeviceProfile
{
    public string Name { get; set; } = "Nettle (mpv, direct play only)";
    public int MaxStreamingBitrate { get; set; } = int.MaxValue;
    public int MaxStaticBitrate { get; set; } = int.MaxValue;
    public List<DirectPlayProfile> DirectPlayProfiles { get; set; } = new() { new() { Type = "Video" }, new() { Type = "Audio" } };
    public List<object> TranscodingProfiles { get; set; } = new();
    public List<object> ContainerProfiles { get; set; } = new();
    public List<object> CodecProfiles { get; set; } = new();
    public List<SubtitleProfile> SubtitleProfiles { get; set; } = BuildSubtitleProfiles();

    private static List<SubtitleProfile> BuildSubtitleProfiles()
    {
        var list = new List<SubtitleProfile>();
        foreach (var f in new[] { "srt", "subrip", "ass", "ssa", "vtt", "webvtt", "sub", "pgs", "pgssub", "dvdsub", "dvbsub", "ttml", "smi", "mov_text" })
            list.Add(new SubtitleProfile { Format = f, Method = "Embed" });
        foreach (var f in new[] { "srt", "subrip", "ass", "ssa", "vtt", "webvtt", "sub", "smi", "ttml" })
            list.Add(new SubtitleProfile { Format = f, Method = "External" });
        return list;
    }
}

public sealed class PlaybackInfoDto
{
    public string? UserId { get; set; }
    public int MaxStreamingBitrate { get; set; } = int.MaxValue;
    public long? StartTimeTicks { get; set; }
    public string? MediaSourceId { get; set; }
    public DeviceProfile DeviceProfile { get; set; } = new();
    public bool EnableDirectPlay { get; set; } = true;
    // Hard rule: Nettle never asks for server-side transcoding or remuxing.
    public bool EnableDirectStream { get; set; } = false;
    public bool EnableTranscoding { get; set; } = false;
    public bool AllowVideoStreamCopy { get; set; } = false;
    public bool AllowAudioStreamCopy { get; set; } = false;
    public bool AutoOpenLiveStream { get; set; } = false;
}

public sealed class PlaybackInfoResponse
{
    public List<MediaSourceInfo> MediaSources { get; set; } = new();
    public string? PlaySessionId { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class PlaybackProgressInfo
{
    public string? ItemId { get; set; }
    public string? MediaSourceId { get; set; }
    public string? PlaySessionId { get; set; }
    public long? PositionTicks { get; set; }
    public bool IsPaused { get; set; }
    public bool IsMuted { get; set; }
    public int? VolumeLevel { get; set; }
    public bool CanSeek { get; set; } = true;
    public string PlayMethod { get; set; } = "DirectPlay";
    public string? EventName { get; set; }
    /// <summary>Jellyfin's index of the audio / subtitle stream playing (-1 = subtitles off).</summary>
    public int? AudioStreamIndex { get; set; }
    public int? SubtitleStreamIndex { get; set; }
}

public sealed class MediaSegmentDto
{
    public string? Id { get; set; }
    public string? ItemId { get; set; }
    public string? Type { get; set; }   // Unknown | Commercial | Preview | Recap | Outro | Intro
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }
}

/// <summary>Legacy Intro Skipper plugin segment (GET /Episode/{id}/IntroSkipperSegments).</summary>
public sealed class IntroSkipperSegment
{
    public string? EpisodeId { get; set; }
    public bool Valid { get; set; }
    public double IntroStart { get; set; }
    public double IntroEnd { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
}

public sealed class PluginInfo
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Id { get; set; }
    public string? Status { get; set; }
}

public sealed class DiscoveredServer
{
    public string? Address { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? EndpointAddress { get; set; }
}
