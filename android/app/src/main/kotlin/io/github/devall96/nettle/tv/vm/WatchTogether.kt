package io.github.devall96.nettle.tv.vm

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.models.SyncPlayGroupInfo
import io.github.devall96.nettle.core.models.SyncPlayQueue
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.core.services.SyncPlayService
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

/** Connects the SyncPlay group to the app (port of Services/WatchTogether.cs). Callbacks arrive on the main thread. */
object WatchTogether {
    /** Bumped whenever the group changes, so screens showing it recompose. */
    var version by mutableIntStateOf(0); private set
    private var queueVersion = 0

    fun attach(sync: SyncPlayService) {
        sync.onQueue = { q -> AppServices.scope.launch { onQueue(sync, q) } }
        sync.onCommand = { c -> AppServices.scope.launch { Main.player?.applySyncCommand(c) } }
        sync.onNotice = { text -> AppServices.scope.launch { Main.player?.flash(text, 3500) ?: Main.toast(text, 3500) } }
        sync.onChanged = { AppServices.scope.launch { version++; Main.player?.updateSyncLabel() } }
    }

    /** The group's queue changed: open its current item here (unless it's already what's on screen). */
    private suspend fun onQueue(sync: SyncPlayService, q: SyncPlayQueue) {
        val current = q.current ?: return
        if (Main.player?.syncEntry == current.playlistItemId) return
        val v = ++queueVersion
        try {
            val item = AppServices.jellyfin.getItem(current.itemId)
            if (v != queueVersion || sync.queue?.current?.playlistItemId != current.playlistItemId) return
            Main.openSyncPlay(item, q.startPositionTicks, current.playlistItemId)
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            Log.warn("SyncPlay: couldn't open the group's item: ${e.message}")
            Main.toast("The group is playing something this profile can't open: ${e.message}", 5000)
        }
    }
}

class SyncGroup(val info: SyncPlayGroupInfo) {
    val people: String get() = if (info.participants.isEmpty()) "Nobody yet" else info.participants.joinToString(", ")
}

/** Watch together (Jellyfin SyncPlay): start or join a group; what anyone plays then plays for everyone. */
class WatchTogetherModel : Page() {
    override val navKey = "together"
    val groups = mutableStateListOf<SyncGroup>()
    var noGroups by mutableStateOf(false); private set
    private val sync get() = AppServices.syncPlay

    val inGroup get() = WatchTogether.version.let { sync.inGroup }
    val groupName get() = WatchTogether.version.let { sync.group?.name }
    val groupPeople get() = WatchTogether.version.let { sync.group?.participants?.joinToString(", ") }
    val groupState get() = WatchTogether.version.let { describe(sync.state) }

    override suspend fun load() = refreshList()
    override suspend fun refresh() = refreshList()

    private suspend fun refreshList() = run {
        val list = sync.list()
        groups.clear()
        groups.addAll(list.filter { it.groupId != sync.group?.groupId }.map { SyncGroup(it) })
        noGroups = groups.isEmpty()
    }

    fun create() {
        launch {
            sync.create("${AppServices.session.current?.userName ?: "Nettle"}'s group")
            Main.toast("Group started. Anything you play now plays for everyone who joins.", 4500)
        }
    }

    fun join(g: SyncGroup) {
        launch {
            sync.join(g.info.groupId)
            Main.toast("Joined ${g.info.name}", 3000)
            refreshList()
        }
    }

    fun leave() {
        launch {
            sync.leave()
            Main.toast("You left the group", 2500)
            refreshList()
        }
    }

    fun reload() { launch { refreshList() } }

    companion object {
        fun describe(state: String) = when (state) {
            "Playing" -> "Playing"
            "Paused" -> "Paused"
            "Waiting" -> "Waiting for everyone to be ready"
            else -> "Nothing playing yet"
        }
    }
}
