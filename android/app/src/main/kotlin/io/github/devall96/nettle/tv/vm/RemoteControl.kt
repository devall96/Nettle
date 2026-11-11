package io.github.devall96.nettle.tv.vm

import io.github.devall96.nettle.core.api.ItemQuery
import io.github.devall96.nettle.core.models.ClientCapabilitiesDto
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*
import kotlinx.serialization.json.*

/**
 * Acts on what the server sends over its WebSocket (port of RemoteControl.cs): other Jellyfin
 * apps controlling this one (play, pause, seek, next, volume, tracks, messages), and changes
 * made elsewhere (watched state, new items) so the screen stays current. Also tells the server
 * what Nettle can do, so it appears as a "Play on" target in other apps.
 */
object RemoteControl {
    private val commands = listOf(
        "DisplayMessage", "SetVolume", "VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute",
        "SetAudioStreamIndex", "SetSubtitleStreamIndex", "DisplayContent",
        "GoHome", "GoToSearch", "GoToSettings", "Back", "PlayState", "Play", "PlayNext", "PlayMediaSource",
    )
    private var lastRefresh = 0L
    private var refreshQueued = false
    private val main = AppServices.scope

    fun start() {
        val s = AppServices.socket
        s.onMessage = { type, data -> main.launch { handle(type, data) } }
        s.onConnected = { main.launch { announce() } }
        s.start(AppServices.scope)
    }

    private suspend fun announce() {
        try {
            AppServices.jellyfin.postCapabilities(ClientCapabilitiesDto(listOf("Video"), commands, supportsMediaControl = true))
        } catch (e: CancellationException) { throw e } catch (e: Exception) { Log.warn("couldn't announce remote-control support: ${e.message}") }
    }

    private fun handle(type: String, data: JsonElement?) {
        if (AppServices.syncPlay.handle(type, data)) return // watch together
        when (type) {
            "Play" -> main.launch { play(data) }
            "Playstate" -> playstate(data)
            "GeneralCommand" -> general(data)
            "UserDataChanged" -> refreshSoon(2000)
            "LibraryChanged" -> refreshSoon(8000)
        }
    }

    /** Refreshes the page on screen once a burst of changes has settled (not while playing). */
    private fun refreshSoon(settleMs: Long) {
        if (refreshQueued) return
        refreshQueued = true
        main.launch {
            try {
                delay(settleMs)
                if (System.currentTimeMillis() - lastRefresh < 5000) delay(5000)
            } finally { refreshQueued = false }
            if (Main.player != null) return@launch // the page refreshes itself when the player closes
            lastRefresh = System.currentTimeMillis()
            Main.shell?.refreshCurrent()
        }
    }

    private fun str(e: JsonElement?, name: String): String? = ((e as? JsonObject)?.get(name) as? JsonPrimitive)?.takeIf { it.isString }?.content
    private fun long(e: JsonElement?, name: String): Long? = ((e as? JsonObject)?.get(name) as? JsonPrimitive)?.let { it.longOrNull ?: it.content.toLongOrNull() }

    private suspend fun play(data: JsonElement?) {
        val ids = ((data as? JsonObject)?.get("ItemIds") as? JsonArray)?.mapNotNull { (it as? JsonPrimitive)?.contentOrNull?.takeIf { s -> s.isNotEmpty() } } ?: return
        if (ids.isEmpty()) return
        val command = str(data, "PlayCommand") ?: "PlayNow"
        val startTicks = long(data, "StartPositionTicks")
        val startIndex = (long(data, "StartIndex") ?: 0).toInt()
        try {
            // One request for all of them; the server returns them in its own order, so put the list back in order.
            val res = AppServices.jellyfin.getItems(ItemQuery(ids = ids.joinToString(","), recursive = true, sortBy = null, sortOrder = null, limit = 500))
            val byId = res.items.associateBy { it.id.lowercase() }
            val items = ids.mapNotNull { byId[it.lowercase()] }.filter { it.isPlayable }
            if (items.isEmpty()) return
            Log.info("remote control: $command (${items.size} item(s))")
            val p = Main.player
            if ((command == "PlayNext" || command == "PlayLast") && p != null && p.isVod) {
                p.enqueue(items, next = command == "PlayNext")
                p.flash(if (command == "PlayNext") "Added to play next" else "Added to the queue")
                return
            }
            Main.playQueue(items, startIndex.coerceIn(0, items.size - 1), startTicks?.takeIf { it > 0 })
        } catch (e: CancellationException) { throw e } catch (e: Exception) { Log.warn("remote play failed: ${e.message}") }
    }

    private fun playstate(data: JsonElement?) {
        val p = Main.player ?: return
        when (str(data, "Command")) {
            "Stop" -> p.close()
            "Pause" -> p.remotePause(true)
            "Unpause" -> p.remotePause(false)
            "PlayPause" -> p.remotePause(null)
            "NextTrack" -> p.remoteNext()
            "PreviousTrack" -> p.remotePrevious()
            "Seek" -> long(data, "SeekPositionTicks")?.let { p.remoteSeek(it) }
            "Rewind" -> p.seekBy(-10)
            "FastForward" -> p.seekBy(30)
        }
    }

    private fun general(data: JsonElement?) {
        val name = str(data, "Name")
        val args = (data as? JsonObject)?.get("Arguments")
        val p = Main.player
        when (name) {
            "DisplayMessage" -> {
                val text = str(args, "Text") ?: str(args, "Header")
                if (text.isNullOrBlank()) return
                if (p != null) p.flash(text, 5000) else Main.toast(text, 5000)
            }
            "SetVolume" -> long(args, "Volume")?.let { p?.remoteSetVolume(it.toInt()) }
            "VolumeUp" -> p?.remoteVolumeStep(5)
            "VolumeDown" -> p?.remoteVolumeStep(-5)
            "Mute" -> p?.remoteMute(true)
            "Unmute" -> p?.remoteMute(false)
            "ToggleMute" -> p?.remoteMute(null)
            "SetAudioStreamIndex" -> long(args, "Index")?.let { p?.selectByStreamIndex(it.toInt(), audio = true) }
            "SetSubtitleStreamIndex" -> long(args, "Index")?.let { p?.selectByStreamIndex(it.toInt(), audio = false) }
            "DisplayContent" -> if (p == null) str(args, "ItemId")?.let { Main.shell?.navigate(DetailsModel(it)) }
            "GoHome" -> if (p == null) Main.shell?.goHome()
            "GoToSearch" -> if (p == null) Main.shell?.openSearchWith("")
            "GoToSettings" -> if (p == null) Main.shell?.navigateRoot(SettingsModel())
            "Back" -> if (p != null) p.close() else Main.shell?.goBack()
        }
    }
}
