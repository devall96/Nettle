package io.github.devall96.nettle.core.models

import kotlinx.serialization.Serializable
import java.util.UUID

/** Port of Nettle.Core.Models.AppSettings (same names, so the two apps stay easy to compare). */
@Serializable
enum class SegmentAction { Off, ShowButton, AutoSkip }

/** Android TV: switch the TV's output to suit the video. */
@Serializable
enum class FrameRateMatching { Off, RefreshRate, RefreshRateAndResolution }

@Serializable
data class PlaybackSettings(
    var introAction: SegmentAction = SegmentAction.ShowButton,
    var recapAction: SegmentAction = SegmentAction.ShowButton,
    var creditsAction: SegmentAction = SegmentAction.ShowButton,
    var previewAction: SegmentAction = SegmentAction.ShowButton,
    var commercialAction: SegmentAction = SegmentAction.AutoSkip,
    var autoPlayNextEpisode: Boolean = true,
    var upNextCountdownSeconds: Int = 10,
    var preferredAudioLanguage: String = "",
    var preferredSubtitleLanguage: String = "",
    /** "" = follow the user's Jellyfin profile; else Default, Always, OnlyForced, Smart or None. */
    var subtitleMode: String = "",
    /** Playback speed kept between files (1 = normal). */
    var playbackSpeed: Double = 1.0,
    /** Subtitle size (1 = normal). */
    var subtitleScale: Double = 1.0,
    /** How many seconds of video to keep downloaded ahead. */
    var networkBufferSeconds: Int = 10,
    var seekStepSeconds: Int = 10,
    var volume: Int = 100,
    /** Send Dolby / DTS audio untouched to the TV / AV receiver over HDMI. */
    var audioPassthrough: Boolean = false,
    /** Ask "Are you still watching?" after several episodes (or hours of live TV) with no input. */
    var stillWatchingPrompt: Boolean = true,
    /** Episodes auto-played in a row, with no input, before the prompt appears. */
    var stillWatchingEpisodes: Int = 3,
    /**
     * NostalgiaTV: play each scheduled program's original file from Jellyfin at the broadcast
     * offset (full quality, nothing re-encoded) instead of NostalgiaTV's broadcast feed.
     */
    var ntvDirectPlay: Boolean = true,
    /** Android TV: match the TV's refresh rate (and optionally resolution) to the video. */
    var frameRateMatching: FrameRateMatching = FrameRateMatching.RefreshRate,
)

@Serializable
data class AppSettings(
    var theme: String = "midnight",
    var autoLogin: Boolean = true,
    var lastAccountId: String? = null,
    var playback: PlaybackSettings = PlaybackSettings(),
    var seerrUrl: String? = null,
    var ntvUrl: String? = null,
    /** "http" or "https": picked on the Connect screen, where only the IP / name is typed. */
    var serverScheme: String = "http",
    var reduceMotion: Boolean = false,
    /** Privacy: allow Requests posters to load directly from image.tmdb.org (third party). */
    var allowDirectTmdbImages: Boolean = false,
    /** Android TV: width of the drawing canvas in Nettle units (1440 = the Windows app's proportions). */
    var interfaceWidth: Int = 1440,
)

/** A remembered sign-in. Stored only inside the encrypted secure store. */
@Serializable
data class SavedAccount(
    val id: String = newId(),
    var serverUrl: String = "",
    var serverId: String? = null,
    var serverName: String? = null,
    var userId: String = "",
    var userName: String = "",
    var avatarTag: String? = null,
    var accessToken: String = "",
    /** Each account gets its own device id: Jellyfin revokes older tokens issued to the same device id. */
    var deviceId: String = newId(),
    var isAdmin: Boolean = false,
    /** Epoch ms. */
    var lastUsed: Long = System.currentTimeMillis(),
    /** The user's Jellyfin playback preferences as last seen, so they apply before the server answers at startup. */
    var userConfig: UserConfiguration? = null,
) {
    companion object {
        fun newId(): String = UUID.randomUUID().toString().replace("-", "")
    }
}

@Serializable
data class AccountVault(val accounts: MutableList<SavedAccount> = mutableListOf())

/** Seerr / NostalgiaTV secrets, kept in the secure store (never in settings.json). */
@Serializable
data class IntegrationSecrets(
    var seerrApiKey: String? = null,
    var seerrSession: String? = null,
    var ntvApiKey: String? = null,
)
