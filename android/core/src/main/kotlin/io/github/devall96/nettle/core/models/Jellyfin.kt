package io.github.devall96.nettle.core.models

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

// Minimal subset of the Jellyfin REST API DTOs that Nettle needs (port of Nettle.Core.Models.Jellyfin).
// Property names are the server's PascalCase names in camelCase; NettleJson.jellyfin maps them.

@Serializable
data class PublicSystemInfo(
    val localAddress: String? = null,
    val serverName: String? = null,
    val version: String? = null,
    val productName: String? = null,
    val id: String? = null,
    val startupWizardCompleted: Boolean = false,
)

@Serializable
data class UserPolicy(val isAdministrator: Boolean = false)

@Serializable
data class UserDto(
    val name: String? = null,
    val serverId: String? = null,
    val id: String = "",
    val primaryImageTag: String? = null,
    val hasPassword: Boolean = false,
    val hasConfiguredPassword: Boolean = false,
    val policy: UserPolicy? = null,
    val configuration: UserConfiguration? = null,
)

/** The user's own playback preferences, as set in their Jellyfin profile. */
@Serializable
data class UserConfiguration(
    /** ISO 639-2 code (e.g. "eng"), or empty for "any". */
    val audioLanguagePreference: String? = null,
    val subtitleLanguagePreference: String? = null,
    /** "Default", "Always", "OnlyForced", "None" or "Smart". */
    val subtitleMode: String? = null,
    /** Libraries the user hid from "Recently added" in their Jellyfin home settings. */
    val latestItemsExcludes: List<String>? = null,
)

@Serializable
data class AuthenticateUserByName(val username: String? = null, val pw: String? = null)

@Serializable
data class AuthenticationResult(val user: UserDto? = null, val accessToken: String? = null, val serverId: String? = null)

@Serializable
data class QuickConnectResult(val authenticated: Boolean = false, val secret: String? = null, val code: String? = null)

@Serializable
data class QuickConnectDto(val secret: String? = null)

@Serializable
data class QueryResult<T>(val items: List<T> = emptyList(), val totalRecordCount: Int = 0, val startIndex: Int = 0)

@Serializable
data class UserItemDataDto(
    val playedPercentage: Double? = null,
    val playbackPositionTicks: Long = 0,
    val played: Boolean = false,
    val isFavorite: Boolean = false,
    val unplayedItemCount: Int? = null,
)

@Serializable
data class BaseItemPerson(
    val name: String? = null,
    val id: String? = null,
    val role: String? = null,
    val type: String? = null,
    val primaryImageTag: String? = null,
)

@Serializable
data class NameGuidPair(val name: String? = null, val id: String? = null)

@Serializable
data class ChapterInfo(val startPositionTicks: Long = 0, val name: String? = null, val imageTag: String? = null)

@Serializable
data class MediaStream(
    val index: Int = 0,
    /** Video | Audio | Subtitle | EmbeddedImage */
    val type: String? = null,
    val codec: String? = null,
    val profile: String? = null,
    val language: String? = null,
    val title: String? = null,
    val displayTitle: String? = null,
    val isDefault: Boolean = false,
    val isForced: Boolean = false,
    val isExternal: Boolean = false,
    val deliveryUrl: String? = null,
    val width: Int? = null,
    val height: Int? = null,
    val videoRange: String? = null,
    val videoRangeType: String? = null,
    val channels: Int? = null,
    val channelLayout: String? = null,
    val bitRate: Int? = null,
    val realFrameRate: Float? = null,
    val averageFrameRate: Float? = null,
)

@Serializable
data class MediaSourceInfo(
    val id: String? = null,
    val name: String? = null,
    val path: String? = null,
    val protocol: String? = null,
    val container: String? = null,
    val size: Long? = null,
    val bitrate: Int? = null,
    val runTimeTicks: Long? = null,
    val supportsDirectPlay: Boolean = false,
    val isRemote: Boolean = false,
    val liveStreamId: String? = null,
    val defaultAudioStreamIndex: Int? = null,
    val defaultSubtitleStreamIndex: Int? = null,
    val mediaStreams: List<MediaStream> = emptyList(),
)

@Serializable
data class BaseItemDto(
    val id: String = "",
    val name: String? = null,
    val originalTitle: String? = null,
    val serverId: String? = null,
    val type: String? = null,
    val mediaType: String? = null,
    val collectionType: String? = null,
    val isFolder: Boolean = false,
    val overview: String? = null,
    val taglines: List<String>? = null,
    val genres: List<String>? = null,
    val studios: List<NameGuidPair>? = null,
    val people: List<BaseItemPerson>? = null,
    val productionYear: Int? = null,
    /** ISO-8601 date-time, as sent by the server. */
    val premiereDate: String? = null,
    val endDate: String? = null,
    val status: String? = null,
    val officialRating: String? = null,
    val communityRating: Float? = null,
    val criticRating: Float? = null,
    val runTimeTicks: Long? = null,
    val parentId: String? = null,
    val seriesId: String? = null,
    val seriesName: String? = null,
    val seasonId: String? = null,
    val seasonName: String? = null,
    val indexNumber: Int? = null,
    val parentIndexNumber: Int? = null,
    val childCount: Int? = null,
    val primaryImageAspectRatio: Double? = null,
    val imageTags: Map<String, String>? = null,
    val backdropImageTags: List<String>? = null,
    val parentBackdropItemId: String? = null,
    val parentBackdropImageTags: List<String>? = null,
    val parentLogoItemId: String? = null,
    val parentLogoImageTag: String? = null,
    val parentThumbItemId: String? = null,
    val parentThumbImageTag: String? = null,
    val seriesPrimaryImageTag: String? = null,
    val userData: UserItemDataDto? = null,
    val mediaSources: List<MediaSourceInfo>? = null,
    val mediaStreams: List<MediaStream>? = null,
    val chapters: List<ChapterInfo>? = null,
    val mediaSourceCount: Int? = null,
    /** Scrubbing thumbnails: media source id → thumbnail width → tile sheet layout. */
    val trickplay: Map<String, Map<String, TrickplayInfo>>? = null,
) {
    val isSeries get() = type == "Series"
    val isEpisode get() = type == "Episode"
    val isSeason get() = type == "Season"
    val isMovie get() = type == "Movie"

    /** Music is not a feature yet, so music items never count as playable (see [Music]). */
    val isPlayable get() = type in PlayableTypes

    fun tag(imageType: String): String? = imageTags?.get(imageType)

    companion object {
        val PlayableTypes = setOf("Movie", "Episode", "Video", "TvChannel", "Trailer")
    }
}

/** Music and music videos are hidden everywhere until they are a real feature. */
object Music {
    val collectionTypes = setOf("music", "musicvideos", "audiobooks")
    val itemTypes = setOf("MusicAlbum", "Audio", "MusicArtist", "MusicVideo", "AudioBook")
    fun isHidden(item: BaseItemDto): Boolean = item.collectionType in collectionTypes || item.type in itemTypes
}

/**
 * Jellyfin 10.9+ trickplay: thumbnails every [interval] ms, packed into JPEG sheets of
 * [tileWidth] × [tileHeight] thumbnails each.
 */
@Serializable
data class TrickplayInfo(
    val width: Int = 0,
    val height: Int = 0,
    val tileWidth: Int = 0,
    val tileHeight: Int = 0,
    val thumbnailCount: Int = 0,
    val interval: Int = 0,
    val bandwidth: Int = 0,
)

@Serializable
data class DirectPlayProfile(val type: String = "Video")

@Serializable
data class SubtitleProfile(val format: String = "", val method: String = "External")

/**
 * A deliberately permissive profile: Nettle plays the original file itself, so it tells the
 * server "direct play everything" and never offers any transcoding profile.
 */
@Serializable
data class DeviceProfile(
    val name: String = "Nettle (Media3, direct play only)",
    val maxStreamingBitrate: Int = Int.MAX_VALUE,
    val maxStaticBitrate: Int = Int.MAX_VALUE,
    val directPlayProfiles: List<DirectPlayProfile> = listOf(DirectPlayProfile("Video"), DirectPlayProfile("Audio")),
    val transcodingProfiles: List<JsonElement> = emptyList(),
    val containerProfiles: List<JsonElement> = emptyList(),
    val codecProfiles: List<JsonElement> = emptyList(),
    val subtitleProfiles: List<SubtitleProfile> = buildSubtitleProfiles(),
) {
    companion object {
        fun buildSubtitleProfiles(): List<SubtitleProfile> =
            listOf("srt", "subrip", "ass", "ssa", "vtt", "webvtt", "sub", "pgs", "pgssub", "dvdsub", "dvbsub", "ttml", "smi", "mov_text")
                .map { SubtitleProfile(it, "Embed") } +
                listOf("srt", "subrip", "ass", "ssa", "vtt", "webvtt", "sub", "smi", "ttml").map { SubtitleProfile(it, "External") }
    }
}

@Serializable
data class PlaybackInfoDto(
    val userId: String? = null,
    val maxStreamingBitrate: Int = Int.MAX_VALUE,
    val startTimeTicks: Long? = null,
    val mediaSourceId: String? = null,
    val deviceProfile: DeviceProfile = DeviceProfile(),
    val enableDirectPlay: Boolean = true,
    // Hard rule: Nettle never asks for server-side transcoding or remuxing.
    val enableDirectStream: Boolean = false,
    val enableTranscoding: Boolean = false,
    val allowVideoStreamCopy: Boolean = false,
    val allowAudioStreamCopy: Boolean = false,
    val autoOpenLiveStream: Boolean = false,
)

@Serializable
data class PlaybackInfoResponse(
    val mediaSources: List<MediaSourceInfo> = emptyList(),
    val playSessionId: String? = null,
    val errorCode: String? = null,
)

@Serializable
data class PlaybackProgressInfo(
    val itemId: String? = null,
    val mediaSourceId: String? = null,
    val playSessionId: String? = null,
    val positionTicks: Long? = null,
    val isPaused: Boolean = false,
    val isMuted: Boolean = false,
    val volumeLevel: Int? = null,
    val canSeek: Boolean = true,
    val playMethod: String = "DirectPlay",
    val eventName: String? = null,
    /** Jellyfin's index of the audio / subtitle stream playing (-1 = subtitles off). */
    val audioStreamIndex: Int? = null,
    val subtitleStreamIndex: Int? = null,
)

@Serializable
data class MediaSegmentDto(
    val id: String? = null,
    val itemId: String? = null,
    /** Unknown | Commercial | Preview | Recap | Outro | Intro */
    val type: String? = null,
    val startTicks: Long = 0,
    val endTicks: Long = 0,
)

/** Legacy Intro Skipper plugin segment (GET /Episode/{id}/IntroSkipperSegments). */
@Serializable
data class IntroSkipperSegment(
    val episodeId: String? = null,
    val valid: Boolean = false,
    val introStart: Double = 0.0,
    val introEnd: Double = 0.0,
    val start: Double = 0.0,
    val end: Double = 0.0,
)

@Serializable
data class PluginInfo(
    val name: String? = null,
    val version: String? = null,
    val description: String? = null,
    val id: String? = null,
    val status: String? = null,
)

@Serializable
data class DiscoveredServer(
    val address: String? = null,
    val id: String? = null,
    val name: String? = null,
    val endpointAddress: String? = null,
)

/** A subtitle the server's subtitle provider plugins found (GET /Items/{id}/RemoteSearch/Subtitles/{lang}). */
@Serializable
data class RemoteSubtitleInfo(
    val id: String? = null,
    val name: String? = null,
    val providerName: String? = null,
    val format: String? = null,
    val author: String? = null,
    val comment: String? = null,
    val threeLetterISOLanguageName: String? = null,
    val downloadCount: Int? = null,
    val communityRating: Float? = null,
    val isHashMatch: Boolean? = null,
    val hearingImpaired: Boolean? = null,
    val forced: Boolean? = null,
)

/** What this client can do when another Jellyfin app controls it (POST /Sessions/Capabilities/Full). */
@Serializable
data class ClientCapabilitiesDto(
    val playableMediaTypes: List<String> = emptyList(),
    val supportedCommands: List<String> = emptyList(),
    val supportsMediaControl: Boolean = false,
    val supportsPersistentIdentifier: Boolean = true,
)

/**
 * The last home screen this account saw, so the next start can paint it at once while fresh
 * data loads. Kept in the cache folder and deleted when the account is removed.
 */
@Serializable
data class HomeSnapshot(
    val views: List<BaseItemDto> = emptyList(),
    val resume: List<BaseItemDto> = emptyList(),
    val nextUp: List<BaseItemDto> = emptyList(),
    /** Library id → its "recently added" items. */
    val latest: Map<String, List<BaseItemDto>> = emptyMap(),
)
