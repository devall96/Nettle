package io.github.devall96.nettle.core.services

import io.github.devall96.nettle.core.api.ApiException
import io.github.devall96.nettle.core.api.ItemQuery
import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.NtvSlot
import io.github.devall96.nettle.core.models.PlaybackInfoResponse
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.abs

/**
 * NostalgiaTV's schedule names Jellyfin items by id, but Jellyfin re-creates items (new id)
 * when a file is replaced or re-scanned, and NostalgiaTV's index can lag behind. When the id
 * is stale we find the same movie / episode by its metadata and play that instead.
 */
class NtvItemResolver(private val jf: JellyfinClient) {
    private val remap = ConcurrentHashMap<String, String>()

    /** Gets playback info for the slot, transparently fixing a stale item id. */
    suspend fun getPlayback(slot: NtvSlot): Pair<String, PlaybackInfoResponse> {
        val ntvId = slot.jellyfinItem()?.second ?: ""
        val id = remap[ntvId] ?: ntvId
        return try {
            id to jf.getPlaybackInfo(id, null)
        } catch (e: ApiException) {
            if (e.statusCode != 404) throw e
            val resolved = findByMetadata(slot)
                ?: throw ApiException("“${slot.displayTitle}” isn't in your Jellyfin library under the id NostalgiaTV expects, and no match was found by title.", 404)
            remap[ntvId] = resolved
            Log.info("NTV stale item id $ntvId → $resolved (${slot.displayTitle})")
            resolved to jf.getPlaybackInfo(resolved, null)
        }
    }

    private fun norm(s: String?) = (s ?: "").lowercase().filter { it.isLetterOrDigit() }

    private suspend fun findByMetadata(slot: NtvSlot): String? {
        if (slot.type == "episode" && slot.seasonNumber != null && slot.episodeNumber != null) {
            val show = slot.showTitle ?: slot.title
            val series = jf.getItems(ItemQuery(searchTerm = show, includeItemTypes = "Series", limit = 20, sortBy = null, sortOrder = null))
            val match = series.items.firstOrNull { norm(it.name) == norm(show) } ?: series.items.firstOrNull() ?: return null
            // Ask for just that episode instead of downloading the whole series.
            return jf.findEpisode(match.id, slot.seasonNumber, slot.episodeNumber)?.id
        }
        val title = slot.title ?: slot.showTitle
        if (title.isNullOrEmpty()) return null
        val res = jf.getItems(ItemQuery(searchTerm = title, includeItemTypes = "Movie,Video", limit = 30, sortBy = null, sortOrder = null))
        val named = res.items.filter { norm(it.name) == norm(title) }
        val best = named.firstOrNull { slot.year == null || it.productionYear == slot.year }
            ?: named.firstOrNull { slot.year != null && it.productionYear != null && abs(it.productionYear - slot.year) <= 1 }
            ?: named.singleOrNull()
        return best?.id
    }
}
