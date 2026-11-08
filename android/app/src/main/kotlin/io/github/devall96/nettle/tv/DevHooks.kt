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
        if (!BuildConfig.DEBUG || intent == null) return
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
