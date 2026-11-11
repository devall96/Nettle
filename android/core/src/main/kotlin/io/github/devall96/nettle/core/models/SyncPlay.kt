package io.github.devall96.nettle.core.models

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.longOrNull
import java.time.Instant
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter

// Port of Nettle.Core/Models/SyncPlay.cs: watch together (Jellyfin SyncPlay), read from the server's /SyncPlay
// endpoints and its "SyncPlayGroupUpdate" / "SyncPlayCommand" WebSocket messages.

/** A watch-together group as the server lists it. */
data class SyncPlayGroupInfo(val groupId: String, val name: String, val state: String, val participants: List<String>) {
    companion object {
        fun from(e: JsonElement?): SyncPlayGroupInfo? {
            val o = e as? JsonObject ?: return null
            val id = SyncPlayJson.str(o, "GroupId") ?: return null
            val people = (o["Participants"] as? JsonArray)?.mapNotNull { (it as? JsonPrimitive)?.takeIf { p -> p.isString }?.content } ?: emptyList()
            return SyncPlayGroupInfo(id, SyncPlayJson.str(o, "GroupName") ?: "Group", SyncPlayJson.str(o, "State") ?: "Idle", people)
        }
    }
}

/** One entry of the group's play queue (the same item can be queued twice, so entries have their own id). */
data class SyncPlayQueueItem(val itemId: String, val playlistItemId: String)

/** The group's play queue: what's on, and from where. Sent whenever it changes. */
data class SyncPlayQueue(val reason: String, val playlist: List<SyncPlayQueueItem>, val playingIndex: Int, val startPositionTicks: Long, val isPlaying: Boolean) {
    val current: SyncPlayQueueItem? get() = playlist.getOrNull(playingIndex)

    companion object {
        fun from(e: JsonElement?): SyncPlayQueue? {
            val o = e as? JsonObject ?: return null
            val list = (o["Playlist"] as? JsonArray)?.mapNotNull { x ->
                val xo = x as? JsonObject ?: return@mapNotNull null
                val item = SyncPlayJson.str(xo, "ItemId") ?: return@mapNotNull null
                val entry = SyncPlayJson.str(xo, "PlaylistItemId") ?: return@mapNotNull null
                SyncPlayQueueItem(item, entry)
            } ?: emptyList()
            return SyncPlayQueue(SyncPlayJson.str(o, "Reason") ?: "", list, (SyncPlayJson.long(o, "PlayingItemIndex") ?: -1).toInt(),
                SyncPlayJson.long(o, "StartPositionTicks") ?: 0, SyncPlayJson.bool(o, "IsPlaying") ?: false)
        }
    }
}

/** A command for everyone in the group, to happen at [whenMs] (the server's clock, Unix ms) at [positionTicks]. */
data class SyncPlayCommand(val command: String, val whenMs: Long, val positionTicks: Long, val playlistItemId: String?) {
    companion object {
        fun from(e: JsonElement?): SyncPlayCommand? {
            val o = e as? JsonObject ?: return null
            val cmd = SyncPlayJson.str(o, "Command") ?: return null
            return SyncPlayCommand(cmd, SyncPlayJson.time(o, "When") ?: System.currentTimeMillis(), SyncPlayJson.long(o, "PositionTicks") ?: 0,
                SyncPlayJson.str(o, "PlaylistItemId"))
        }
    }
}

object SyncPlayJson {
    fun str(o: JsonObject, name: String): String? = (o[name] as? JsonPrimitive)?.takeIf { it.isString }?.content
    fun long(o: JsonObject, name: String): Long? = (o[name] as? JsonPrimitive)?.takeIf { !it.isString }?.longOrNull
    fun bool(o: JsonObject, name: String): Boolean? = (o[name] as? JsonPrimitive)?.takeIf { !it.isString }?.booleanOrNull
    fun time(o: JsonObject, name: String): Long? = parseTime(str(o, name))

    /** ISO 8601 → Unix ms (UTC when no zone is given, as Jellyfin's times are). */
    fun parseTime(s: String?): Long? {
        if (s.isNullOrBlank()) return null
        return runCatching { OffsetDateTime.parse(s).toInstant().toEpochMilli() }.getOrNull()
            ?: runCatching { Instant.parse(s + "Z").toEpochMilli() }.getOrNull()
    }

    /** How the server wants times: ISO 8601, UTC. */
    fun format(ms: Long): String = DateTimeFormatter.ISO_INSTANT.format(Instant.ofEpochMilli(ms))
}

enum class SyncCorrectionKind { None, Speed, Skip }

data class SyncCorrection(val kind: SyncCorrectionKind, val speed: Double = 1.0, val durationMs: Double = 0.0, val targetMs: Double = 0.0)

/** The arithmetic of staying together (same numbers as the desktop's SyncPlayMath). */
object SyncPlayMath {
    /** Closer than this counts as together. */
    const val TOLERANCE_MS = 120.0
    /** Further apart than this, jump instead of catching up gently. */
    const val SKIP_ABOVE_MS = 2000.0
    /** Catching up by speed takes about this long, within the speed limits below. */
    const val CATCH_UP_OVER_MS = 4000.0
    const val MAX_SPEED_CHANGE = 0.12

    /** The server clock minus this one, and the round trip (ms), from one /GetUtcTime exchange (NTP formula). */
    fun clockSample(t0: Long, t1: Long, t2: Long, t3: Long): Pair<Long, Long> = (((t1 - t0) + (t2 - t3)) / 2) to ((t3 - t0) - (t2 - t1))

    /** Where the group's video is at [serverNowMs], after an Unpause command. */
    fun expectedMs(unpause: SyncPlayCommand, serverNowMs: Long): Double =
        unpause.positionTicks / 10_000.0 + maxOf(0L, serverNowMs - unpause.whenMs)

    /** How to get from [actualMs] to [expectedMs]. */
    fun correction(actualMs: Double, expectedMs: Double): SyncCorrection {
        val diff = expectedMs - actualMs // + = this player is behind the group
        if (kotlin.math.abs(diff) < TOLERANCE_MS) return SyncCorrection(SyncCorrectionKind.None)
        if (kotlin.math.abs(diff) >= SKIP_ABOVE_MS) return SyncCorrection(SyncCorrectionKind.Skip, targetMs = expectedMs)
        val change = (diff / CATCH_UP_OVER_MS).coerceIn(-MAX_SPEED_CHANGE, MAX_SPEED_CHANGE)
        return SyncCorrection(SyncCorrectionKind.Speed, speed = 1 + change, durationMs = kotlin.math.abs(diff / change))
    }
}
