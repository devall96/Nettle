package io.github.devall96.nettle.core.services

import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * Port of Nettle.Core/Services/SyncPlayService.cs: this device's membership of a Jellyfin watch-together group.
 * Follows the group from the server's WebSocket messages ([handle]), keeps this clock in step with the server's,
 * and tells the app what to play ([onQueue]) and when to pause / play / seek ([onCommand]).
 */
class SyncPlayService(private val jf: JellyfinClient, private val scope: CoroutineScope) {
    private var clockLoop: Job? = null

    /** The group this device is in (null = not in one). */
    @Volatile var group: SyncPlayGroupInfo? = null; private set
    val inGroup: Boolean get() = group != null
    /** Idle, Waiting (for everyone to be ready), Paused or Playing. */
    @Volatile var state: String = "Idle"; private set
    @Volatile var queue: SyncPlayQueue? = null; private set
    val currentPlaylistItemId: String? get() = queue?.current?.playlistItemId
    /** The last Unpause / Pause / Seek from the group (what the drift check measures against). */
    @Volatile var lastCommand: SyncPlayCommand? = null; private set

    /** The server's clock minus this one, ms. */
    @Volatile var offsetMs: Long = 0; private set
    @Volatile var roundTripMs: Long = 0; private set
    val serverNowMs: Long get() = System.currentTimeMillis() + offsetMs
    fun toLocalMs(serverMs: Long): Long = serverMs - offsetMs

    var onChanged: (() -> Unit)? = null
    var onQueue: ((SyncPlayQueue) -> Unit)? = null
    var onCommand: ((SyncPlayCommand) -> Unit)? = null
    var onNotice: ((String) -> Unit)? = null

    /** Handles a WebSocket message if it's a SyncPlay one; true when it was. */
    fun handle(type: String, data: JsonElement?): Boolean = when (type) {
        "SyncPlayCommand" -> {
            val cmd = SyncPlayCommand.from(data)
            if (cmd != null && inGroup) {
                if (cmd.command == "Unpause" || cmd.command == "Pause" || cmd.command == "Seek") lastCommand = cmd
                Log.info("SyncPlay: ${cmd.command} at ${"%.1f".format(cmd.positionTicks / 10_000_000.0)} s")
                onCommand?.invoke(cmd)
            }
            true
        }
        "SyncPlayGroupUpdate" -> { handleUpdate(data as? JsonObject); true }
        else -> false
    }

    private fun handleUpdate(e: JsonObject?) {
        if (e == null) return
        val data = e["Data"]
        val text = (data as? JsonPrimitive)?.takeIf { it.isString }?.content
        when (SyncPlayJson.str(e, "Type")) {
            "GroupJoined" -> {
                group = SyncPlayGroupInfo.from(data) ?: group
                state = group?.state ?: "Idle"
                Log.info("SyncPlay: joined \"${group?.name}\"")
                startClockSync()
                onChanged?.invoke()
            }
            "UserJoined" -> if (text != null) { participant(text, true); onNotice?.invoke("$text is watching with you") }
            "UserLeft" -> if (text != null) { participant(text, false); onNotice?.invoke("$text left the group") }
            "GroupLeft" -> leftGroup()
            "NotInGroup", "GroupDoesNotExist" -> { if (group != null) onNotice?.invoke("The watch-together group has ended"); leftGroup() }
            "LibraryAccessDenied" -> onNotice?.invoke("Someone in the group can't see this in their libraries, so it can't be played together")
            "StateUpdate" -> { state = (data as? JsonObject)?.let { SyncPlayJson.str(it, "State") } ?: state; onChanged?.invoke() }
            "PlayQueue" -> SyncPlayQueue.from(data)?.let { q ->
                queue = q
                Log.info("SyncPlay: queue ${q.reason}, ${q.playlist.size} item(s), playing #${q.playingIndex} from ${q.startPositionTicks / 10_000_000} s")
                onQueue?.invoke(q)
                onChanged?.invoke()
            }
        }
    }

    private fun participant(name: String, joined: Boolean) {
        val g = group ?: return
        group = g.copy(participants = if (joined) g.participants + name else g.participants - name)
        onChanged?.invoke()
    }

    private fun leftGroup() {
        if (group == null && queue == null) return
        group = null; queue = null; lastCommand = null; state = "Idle"
        clockLoop?.cancel(); clockLoop = null
        Log.info("SyncPlay: left the group")
        onChanged?.invoke()
    }

    // ---------- Joining and leaving ----------

    suspend fun list(): List<SyncPlayGroupInfo> = jf.syncPlayList()
    suspend fun create(name: String) { syncClock(); jf.syncPlayNew(name) }
    suspend fun join(groupId: String) { syncClock(); jf.syncPlayJoin(groupId) }
    suspend fun leave() { try { jf.syncPlayLeave() } finally { leftGroup() } }
    /** Signing out / switching profile: this device is no longer in the group. */
    fun reset() = leftGroup()

    // ---------- Asking the group ----------

    suspend fun play(itemIds: List<String>, index: Int, startTicks: Long) = jf.syncPlaySetNewQueue(itemIds, index, startTicks)
    suspend fun pause() = jf.syncPlayPause()
    suspend fun unpause() = jf.syncPlayUnpause()
    suspend fun seek(ticks: Long) = jf.syncPlaySeek(ticks)
    suspend fun stop() = jf.syncPlayStop()
    suspend fun next() { currentPlaylistItemId?.let { jf.syncPlayNextItem(it) } }
    suspend fun previous() { currentPlaylistItemId?.let { jf.syncPlayPreviousItem(it) } }

    /** This player is loaded and waiting (ready) or stalled (not ready), at [positionTicks]. */
    suspend fun report(ready: Boolean, positionTicks: Long, isPlaying: Boolean) {
        val id = currentPlaylistItemId ?: return
        jf.syncPlayReport(ready, serverNowMs, positionTicks, isPlaying, id)
    }

    // ---------- Clock ----------

    /** Measures this clock against the server's (best of a few tries) and tells the server the delay. */
    suspend fun syncClock(samples: Int = 4) {
        var bestRtt = Long.MAX_VALUE
        var bestOffset = offsetMs
        repeat(samples) {
            try {
                val t0 = System.currentTimeMillis()
                val (t1, t2) = jf.getUtcTime()
                val t3 = System.currentTimeMillis()
                val (offset, rtt) = SyncPlayMath.clockSample(t0, t1, t2, t3)
                if (rtt < bestRtt) { bestRtt = rtt; bestOffset = offset }
            } catch (e: CancellationException) { throw e } catch (e: Exception) { Log.warn("SyncPlay clock sample failed: ${e.message}") }
        }
        if (bestRtt == Long.MAX_VALUE) return
        offsetMs = bestOffset
        roundTripMs = bestRtt
        Log.info("SyncPlay: server clock ${if (offsetMs >= 0) "+" else ""}$offsetMs ms from this one, round trip $roundTripMs ms")
        if (inGroup) try { jf.syncPlayPing(roundTripMs) } catch (e: CancellationException) { throw e } catch (_: Exception) { }
    }

    /** While in a group, the clock is measured again every minute. */
    private fun startClockSync() {
        clockLoop?.cancel()
        clockLoop = scope.launch {
            while (isActive) {
                syncClock()
                delay(60_000)
            }
        }
    }
}
