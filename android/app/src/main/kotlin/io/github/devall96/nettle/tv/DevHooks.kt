package io.github.devall96.nettle.tv

import android.content.Intent
import io.github.devall96.nettle.tv.vm.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Debug builds only (like the desktop's NETTLE_DEV_* variables): lets automated tests open a
 * page or play an item directly, e.g.
 *   adb shell am start -n io.github.devall96.nettle.tv/.MainActivity --es page search --es query night
 *   adb shell am start -n io.github.devall96.nettle.tv/.MainActivity --es play <itemId>
 * Release builds never read these extras.
 */
object DevHooks {
    private suspend fun devSignIn(server: String, user: String) {
        try {
            AppServices.allowHostOf(server) // as the Connect screen does for a typed address
            val (url, info) = AppServices.jellyfin.probeServer(server)
            AppServices.session.beginLogin(url)
            val auth = AppServices.jellyfin.authenticate(user, "")
            AppServices.session.completeLogin(url, info, auth, false)
            Main.showShell()
            io.github.devall96.nettle.core.services.Log.info("dev: signed in to the test server")
        } catch (e: Exception) { io.github.devall96.nettle.core.services.Log.warn("dev: test sign-in failed: ${e.message}") }
    }

    private suspend fun devSyncPlay(spec: String, script: String?) {
        val sync = AppServices.syncPlay
        try {
            repeat(60) { if (Main.shell == null) delay(250) }
            delay(1500)
            val name = spec.substringAfter(':', "Nettle test")
            if (spec.startsWith("create")) sync.create(name)
            else for (i in 0 until 30) {
                if (sync.inGroup) break
                sync.list().firstOrNull { it.name == name }?.let { sync.join(it.groupId) }
                delay(1000)
            }
            io.github.devall96.nettle.core.services.Log.info("dev: SyncPlay $spec done, in group: ${sync.inGroup}")
            if (script.isNullOrBlank()) return
            for (i in 0 until 120) { if (sync.state == "Playing") break; delay(500) }
            val started = System.currentTimeMillis()
            for (step in script.split(',')) {
                val at = step.split('@')
                val sec = at.getOrNull(1)?.toIntOrNull() ?: continue
                val wait = started + sec * 1000L - System.currentTimeMillis()
                if (wait > 0) delay(wait)
                io.github.devall96.nettle.core.services.Log.info("dev: SyncPlay step ${at[0]}")
                val p = Main.player ?: continue
                when {
                    at[0] == "pause" && p.engine.isPlaying -> p.togglePlay()
                    at[0] == "play" && !p.engine.isPlaying -> p.togglePlay()
                    at[0].startsWith("seek:") -> at[0].removePrefix("seek:").toLongOrNull()?.let { p.seekToMs(it * 1000) }
                    at[0] == "leave" -> sync.leave()
                }
            }
        } catch (e: Exception) { io.github.devall96.nettle.core.services.Log.warn("dev: SyncPlay hook failed: ${e.message}") }
    }

    fun handle(intent: Intent?) {
        if (intent == null) return
        // Android keeps the launch that started the task and re-delivers it when the app is reopened from the
        // home screen or recents: act on a test launch once, never again on a reopen.
        if (intent.flags and Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY != 0 || intent.getBooleanExtra("devHandled", false)) return
        intent.putExtra("devHandled", true)
        // Sync-test builds (-Pnettle.syncTest=true) also take: --es synctest aac|ac3 (bundled flash + beep clip).
        // Optional, saved as settings: --es offsets 24=210,60=20 (Audio timing per refresh rate), --es match <FrameRateMatching>.
        if (BuildConfig.SYNC_TEST) intent.getStringExtra("synctest")?.takeIf { it == "aac" || it == "ac3" }?.let { kind ->
            val p = AppServices.settings.current.playback
            // --es offsets 24=180,60=20 (Audio timing per refresh rate)
            intent.getStringExtra("offsets")?.let { s ->
                p.audioOffsets = s.split(',').mapNotNull { e -> e.split('=').takeIf { it.size == 2 }?.let { (k, v) -> v.trim().toIntOrNull()?.let { k.trim() to it } } }.toMap()
            }
            // --es match Off|RefreshRate|RefreshRateAndResolution (Match frame rate)
            intent.getStringExtra("match")?.let { m -> io.github.devall96.nettle.core.models.FrameRateMatching.entries.firstOrNull { it.name == m }?.let { p.frameRateMatching = it } }
            io.github.devall96.nettle.core.services.Log.info("sync test: $kind, audio timing ${p.audioOffsets}, match ${p.frameRateMatching}")
            AppServices.scope.launch {
                repeat(40) { if (Main.shell == null) delay(250) }
                Main.playSyncTest(kind)
            }
            return
        }
        // ... and --es lipsync films|usual (Settings → Lip sync check).
        if (BuildConfig.SYNC_TEST) intent.getStringExtra("lipsync")?.let { which ->
            AppServices.scope.launch {
                repeat(40) { if (Main.shell == null) delay(250) }
                Main.playLipSyncCheck(films = which == "films")
            }
            return
        }
        if (!BuildConfig.DEBUG) return
        // --es crash 1: closes the app with an unhandled error after a moment (tests the "closed unexpectedly" screen).
        intent.getStringExtra("crash")?.let { AppServices.scope.launch { delay(1500); throw IllegalStateException("Test crash from the dev hook") } }
        // --es server https://demo.jellyfin.org/stable [--es user demo]: a test sign-in (empty password, not saved).
        intent.getStringExtra("server")?.let { server -> AppServices.scope.launch { devSignIn(server, intent.getStringExtra("user") ?: "demo") } }
        // --es syncplay create:NAME or join:NAME [--es syncscript pause@10,play@16,seek:300@22]
        intent.getStringExtra("syncplay")?.let { spec -> AppServices.scope.launch { devSyncPlay(spec, intent.getStringExtra("syncscript")) } }
        val page = intent.getStringExtra("page")
        val query = intent.getStringExtra("query")
        val play = intent.getStringExtra("play")
        val details = intent.getStringExtra("details")
        val theme = intent.getStringExtra("theme")
        val library = intent.getStringExtra("library")
        // --es probe 1: one log line with the player's state, for the QA audit to check results.
        if (intent.getStringExtra("probe") != null) { io.github.devall96.nettle.core.services.Log.info("probe: " + (Main.player?.qaState() ?: "no player") + " page=" + (Main.shell?.current?.javaClass?.simpleName ?: "none")); return }
        if (page == null && play == null && details == null && theme == null && library == null) return
        val playDelay = (intent.getStringExtra("playdelay")?.toLongOrNull() ?: 0L) * 1000
        AppServices.scope.launch {
            // Wait until signed in and the shell is up.
            repeat(40) { if (Main.shell == null) delay(250) }
            val shell = Main.shell ?: return@launch
            theme?.let { AppServices.settings.current.theme = it; Main.applySettings() }
            when (page) {
                "home" -> shell.goHome()
                "search" -> shell.navigateRoot(SearchModel().apply { if (query != null) updateQuery(query) })
                "livetv" -> shell.navigateRoot(LiveTvModel())
                "requests" -> shell.navigateRoot(RequestsModel())
                "settings" -> shell.navigateRoot(SettingsModel())
                "together" -> shell.navigateRoot(WatchTogetherModel())
            }
            // --es library <viewId>: open that library page directly (the QA audit).
            library?.let { id -> runCatching { AppServices.jellyfin.getItem(id) }.getOrNull()?.let { shell.navigateRoot(LibraryModel(it)) } }
            details?.let { shell.navigate(DetailsModel(it)) }
            if (playDelay > 0) delay(playDelay)
            // --es startsec N: start N seconds in (or N seconds before the end when negative), to reach Up Next / credits quickly.
            val startSec = intent.getStringExtra("startsec")?.toLongOrNull()
            play?.let { id -> runCatching { AppServices.jellyfin.getItem(id) }.getOrNull()?.let {
                if (startSec != null) {
                    val len = (it.runTimeTicks ?: 0L) / 10_000_000L
                    Main.playItemAt(it, (if (startSec < 0) maxOf(0, len + startSec) else startSec) * 10_000_000L)
                } else Main.playItem(it, resume = intent.getBooleanExtra("resume", false))
            } }
        }
    }
}
