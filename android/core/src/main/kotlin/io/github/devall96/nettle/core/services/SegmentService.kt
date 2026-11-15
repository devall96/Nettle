package io.github.devall96.nettle.core.services

import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.PlaybackSettings
import io.github.devall96.nettle.core.models.SegmentAction

enum class SegmentKind { Intro, Recap, Credits, Preview, Commercial }

/** A skippable part of a video, in milliseconds. */
data class PlaybackSegment(val kind: SegmentKind, val startMs: Long, val endMs: Long) {
    val skipLabel: String
        get() = when (kind) {
            SegmentKind.Intro -> "Skip Intro"
            SegmentKind.Recap -> "Skip Recap"
            SegmentKind.Credits -> "Skip Credits"
            SegmentKind.Preview -> "Skip Preview"
            SegmentKind.Commercial -> "Skip Ad Break"
        }
}

/**
 * Collects skippable segments from server plugins (port of SegmentService.cs). Prefers Jellyfin's
 * Media Segments API (10.10+) and falls back to the Intro Skipper plugin's legacy endpoint.
 */
class SegmentService(private val client: JellyfinClient) {
    /** [legacyFallback]: also ask the Intro Skipper plugin's old endpoint (it only knows episodes). */
    suspend fun getSegments(itemId: String, legacyFallback: Boolean = true): List<PlaybackSegment> {
        val result = ArrayList<PlaybackSegment>()
        for (s in client.getMediaSegments(itemId)) {
            val kind = when (s.type) {
                "Intro" -> SegmentKind.Intro
                "Recap" -> SegmentKind.Recap
                "Outro" -> SegmentKind.Credits
                "Preview" -> SegmentKind.Preview
                "Commercial" -> SegmentKind.Commercial
                else -> null
            }
            if (kind != null && s.endTicks > s.startTicks) result += PlaybackSegment(kind, s.startTicks / 10_000, s.endTicks / 10_000)
        }
        // The plugin's own endpoint is asked whenever the server's list has no intro: some setups publish
        // only part of what Intro Skipper found (credits but no intro), which used to hide Skip Intro.
        if (legacyFallback && result.none { it.kind == SegmentKind.Intro }) {
            val have = result.map { it.kind }.toSet()
            for ((key, v) in client.getIntroSkipperSegments(itemId)) {
                if (!v.valid) continue
                val start = if (v.introEnd > 0) v.introStart else v.start
                val end = if (v.introEnd > 0) v.introEnd else v.end
                if (end <= start) continue
                val kind = when (key) {
                    "Introduction" -> SegmentKind.Intro
                    "Credits" -> SegmentKind.Credits
                    "Recap" -> SegmentKind.Recap
                    "Preview" -> SegmentKind.Preview
                    "Commercial" -> SegmentKind.Commercial
                    else -> SegmentKind.Intro
                }
                if (kind in have) continue // the server's own entry wins
                result += PlaybackSegment(kind, (start * 1000).toLong(), (end * 1000).toLong())
            }
        }
        result.sortBy { it.startMs }
        return result
    }

    /**
     * [getSegments], asked again (after 3 s, then 10 s) when the server couldn't answer. One failed
     * request at the start of an episode used to mean no Skip buttons for all of it.
     */
    suspend fun getSegmentsReliably(itemId: String, legacyFallback: Boolean = true): List<PlaybackSegment> {
        val waits = longArrayOf(3000, 10000)
        var attempt = 0
        while (true) {
            try { return getSegments(itemId, legacyFallback) } catch (e: io.github.devall96.nettle.core.api.ApiException) {
                if (attempt >= waits.size) { Log.warn("skip segments couldn't be loaded: ${e.message}"); return emptyList() }
                Log.info("skip segments not loaded (${e.message}); asking again")
                kotlinx.coroutines.delay(waits[attempt++])
            }
        }
    }

    companion object {
        /**
         * The segment to act on at [posMs]: the first one covering it that isn't switched off or dismissed.
         * (Taking simply the first one let a hidden recap mask the intro it overlaps.)
         */
        fun activeAt(segments: List<PlaybackSegment>, posMs: Long, settings: PlaybackSettings, dismissed: PlaybackSegment?): PlaybackSegment? =
            segments.firstOrNull { posMs >= it.startMs && posMs < it.endMs - 500 && it != dismissed && actionFor(it.kind, settings) != SegmentAction.Off }

        fun actionFor(kind: SegmentKind, s: PlaybackSettings): SegmentAction = when (kind) {
            SegmentKind.Intro -> s.introAction
            SegmentKind.Recap -> s.recapAction
            SegmentKind.Credits -> s.creditsAction
            SegmentKind.Preview -> s.previewAction
            SegmentKind.Commercial -> s.commercialAction
        }
    }
}
