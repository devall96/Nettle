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
        val page = intent.getStringExtra("page")
        val query = intent.getStringExtra("query")
        val play = intent.getStringExtra("play")
        val details = intent.getStringExtra("details")
        val theme = intent.getStringExtra("theme")
        if (page == null && play == null && details == null && theme == null) return
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
            }
            details?.let { shell.navigate(DetailsModel(it)) }
            play?.let { id -> runCatching { AppServices.jellyfin.getItem(id) }.getOrNull()?.let { Main.playItem(it, resume = intent.getBooleanExtra("resume", false)) } }
        }
    }
}
