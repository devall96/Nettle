package io.github.devall96.nettle.core.models

import kotlinx.serialization.Serializable

// NostalgiaTV live channels, read from the server's standard HDHR/IPTV outputs
// (channels.m3u + xmltv.xml). Nettle intentionally exposes ONLY the live guide,
// never NostalgiaTV's on-demand section. Times are Unix epoch milliseconds.

class NtvChannel(
    val id: String,
    val name: String,
    val number: String?,
    var logoUrl: String?,
    val streamUrl: String,
    val group: String?,
) {
    val programs: MutableList<NtvProgram> = ArrayList()

    /** NostalgiaTV's internal channel id (last segment of /stream/{id}); keys the schedule API. */
    val stableId: String?
        get() {
            val i = streamUrl.lastIndexOf("/stream/", ignoreCase = true)
            if (i < 0) return null
            val id = streamUrl.substring(i + 8)
            val q = id.indexOfAny(charArrayOf('?', '/'))
            return if (q >= 0) id.substring(0, q) else id
        }

    fun programAt(t: Long): NtvProgram? {
        // Programs are sorted by start time; binary search for the airing one.
        var lo = 0
        var hi = programs.size - 1
        while (lo <= hi) {
            val mid = (lo + hi) ushr 1
            val p = programs[mid]
            if (t < p.start) hi = mid - 1 else if (t >= p.stop) lo = mid + 1 else return p
        }
        return null
    }

    fun programAfter(t: Long): NtvProgram? = programs.firstOrNull { it.start > t }
}

class NtvProgram(
    val start: Long,
    val stop: Long,
    val title: String,
    val subTitle: String? = null,
    val description: String? = null,
    val episodeNum: String? = null,
    val iconUrl: String? = null,
    val category: String? = null,
) {
    val durationMs: Long get() = stop - start
}

// ---- NostalgiaTV schedule API (/api/schedule?hours=N&channelId=X), camelCase JSON ----
// Each scheduled slot names the media-server item that airs, which lets Nettle play the
// ORIGINAL file from Jellyfin at the broadcast offset instead of NostalgiaTV's re-encoded feed.

@Serializable
data class NtvScheduleResponse(val timestamp: Long = 0, val channels: List<NtvScheduleChannel> = emptyList())

@Serializable
data class NtvScheduleChannel(val stableId: String? = null, val name: String? = null, val programs: List<NtvSlot> = emptyList())

@Serializable
data class NtvSlot(
    val title: String? = null,
    val episodeTitle: String? = null,
    val showTitle: String? = null,
    val seasonNumber: Int? = null,
    val episodeNumber: Int? = null,
    val year: Int? = null,
    val type: String? = null,
    val startTimeMs: Long = 0,
    val endTimeMs: Long = 0,
    val summary: String? = null,
    val ratingKey: String? = null,
    val serverId: String? = null,
    val videoResolution: String? = null,
    val videoProfile: String? = null,
    val playbackStartTimeMs: Long? = null,
) {
    /** ratingKey "jelly:{serverId}:{itemId}" → the Jellyfin server and item, or null. */
    fun jellyfinItem(): Pair<String, String>? {
        val k = ratingKey ?: return null
        if (!k.startsWith("jelly:", ignoreCase = true)) return null
        val parts = k.split(':')
        if (parts.size < 3 || parts[1].isEmpty() || parts[2].isEmpty()) return null
        return parts[1] to parts[2]
    }

    val displaySubtitle: String
        get() {
            val ep = if (seasonNumber != null && episodeNumber != null) "S$seasonNumber:E$episodeNumber" else null
            return listOfNotNull(ep, episodeTitle).filter { it.isNotEmpty() }.joinToString("  ")
        }

    val displayTitle: String get() = showTitle ?: title ?: ""
}
