package io.github.devall96.nettle.tv

import android.app.Application
import android.os.Bundle
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.activity.compose.setContent
import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import io.github.devall96.nettle.tv.screens.*
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.theme.NettleTheme
import io.github.devall96.nettle.tv.theme.Themes
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.*

class NettleApp : Application() {
    override fun onCreate() {
        super.onCreate()
        AppServices.init(this)
    }
}

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Main.leaveApp = { moveTaskToBack(true) }
        readHdrCapabilities()
        if (Main.root == null) Main.start()
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = goBack()
        })
        setContent { NettleRoot() }
        // The window's own background only covers start-up; once Compose has drawn its opaque
        // background it would just be painted underneath every frame, so it is dropped.
        window.decorView.post { window.decorView.postOnAnimation { window.setBackgroundDrawable(null) } }
        if (BuildConfig.DEBUG) DevHooks.handle(intent)
    }

    override fun onNewIntent(intent: android.content.Intent) {
        super.onNewIntent(intent)
        if (BuildConfig.DEBUG) DevHooks.handle(intent)
    }

    /** Which HDR types the TV accepts: decides Dolby Vision vs its HDR10 layer, and what the badges may claim. */
    @Suppress("DEPRECATION")
    private fun readHdrCapabilities() {
        val types = runCatching { windowManager.defaultDisplay.hdrCapabilities?.supportedHdrTypes?.toSet() }.getOrNull() ?: emptySet()
        val caps = AppServices.HdrCaps(
            hdr10 = android.view.Display.HdrCapabilities.HDR_TYPE_HDR10 in types,
            hlg = android.view.Display.HdrCapabilities.HDR_TYPE_HLG in types,
            hdr10Plus = android.view.Display.HdrCapabilities.HDR_TYPE_HDR10_PLUS in types,
            dolbyVision = android.view.Display.HdrCapabilities.HDR_TYPE_DOLBY_VISION in types,
        )
        AppServices.hdrTypes = caps
        AppServices.displaySupportsDolbyVision = caps.dolbyVision
        io.github.devall96.nettle.core.services.Log.info("display HDR types: $caps")
    }

    /** Home button / another app: pause and save the position, like closing the desktop window. */
    override fun onStop() {
        super.onStop()
        Main.player?.onAppStopped()
    }

    /** Back steps back; from Home (or the sign-in screens) it leaves for the launcher. */
    private fun goBack() {
        val shell = Main.shell
        if (Main.player != null) { Main.closePlayer(); return }
        if (shell != null && shell.goBack()) return
        if (Main.root is LoginModel || (Main.root is ServerSelectModel && (Main.root as ServerSelectModel).canGoBack)) { Main.showAccountPicker(); return }
        Main.leaveApp()
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        // Gamepad B behaves like Back, as on other TV apps.
        if (event.keyCode == KeyEvent.KEYCODE_BUTTON_B) {
            if (event.action == KeyEvent.ACTION_UP) goBack()
            return true
        }
        return super.dispatchKeyEvent(event)
    }
}

/** MainView: background + ambient glow, the current root screen, the player on top, splash and toast. */
@Composable
fun NettleRoot() {
    NettleTheme(Themes.find(Main.themeKey), Main.canvasWidth, Main.motionEnabled) {
        val p = Nettle.colors
        // Background + ambient glow in a single opaque pass (see ThemePalette.backgroundWithGlow).
        val bg = remember(p) { Modifier.fillMaxSize().cachedGradients(p.backgroundWithGlow) }
        Box(bg) {
            val player = Main.player
            if (player == null) {
                val root = Main.root
                if (Main.motionEnabled) Crossfade(root, animationSpec = tween(280), label = "root") { RootScreen(it) } else RootScreen(root)
            } else androidx.compose.runtime.key(player) { io.github.devall96.nettle.tv.player.PlayerScreen(player) }
            // Splash
            if (Main.isStarting) Box(Modifier.fillMaxSize().background(p.background), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(22.dp)) {
                    BrandMark(1.6f)
                    Spinner(size = 34.dp)
                    T("Starting…", muted = true)
                }
            }
            // Toast
            Main.toastText?.let {
                Box(
                    Modifier.align(Alignment.BottomCenter).padding(bottom = 48.dp).clip(CircleShape).background(p.surfaceHigh)
                        .border(1.dp, p.border, CircleShape).padding(22.dp, 12.dp),
                ) { T(it, weight = FontWeight.SemiBold) }
            }
        }
    }
}

@Composable
private fun RootScreen(root: ViewModelBase?) {
    when (root) {
        is AccountPickerModel -> AccountPickerScreen(root)
        is ServerSelectModel -> ServerSelectScreen(root)
        is LoginModel -> LoginScreen(root)
        is Shell -> ShellScreen(root)
        else -> {}
    }
}
