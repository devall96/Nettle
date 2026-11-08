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
    suspend fun getSegments(itemId: String): List<PlaybackSegment> {
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
        if (result.isEmpty()) {
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
                result += PlaybackSegment(kind, (start * 1000).toLong(), (end * 1000).toLong())
            }
        }
        result.sortBy { it.startMs }
        return result
    }

    companion object {
        fun actionFor(kind: SegmentKind, s: PlaybackSettings): SegmentAction = when (kind) {
            SegmentKind.Intro -> s.introAction
            SegmentKind.Recap -> s.recapAction
            SegmentKind.Credits -> s.creditsAction
            SegmentKind.Preview -> s.previewAction
            SegmentKind.Commercial -> s.commercialAction
        }
    }
}
