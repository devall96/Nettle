package io.github.devall96.nettle.tv.vm

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.models.BaseItemDto
import io.github.devall96.nettle.core.models.NtvChannel
import io.github.devall96.nettle.core.models.PublicSystemInfo
import io.github.devall96.nettle.core.models.SavedAccount
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.core.services.SessionCheck
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.player.PlayerModel
import kotlinx.coroutines.*

/** Root of the app (port of MainViewModel): splash → sign-in flow → shell, with the player on top. */
object Main {
    var root by mutableStateOf<ViewModelBase?>(null); private set
    var player by mutableStateOf<PlayerModel?>(null); private set
    var isStarting by mutableStateOf(true); private set
    var toastText by mutableStateOf<String?>(null); private set
    var themeKey by mutableStateOf(io.github.devall96.nettle.tv.theme.Themes.DEFAULT_KEY)
    var motionEnabled by mutableStateOf(true)
    var canvasWidth by mutableStateOf(1440)

    val shell: Shell? get() = root as? Shell
    private val scope = AppServices.scope
    private var toastJob: Job? = null

    /** Leave the app for the launcher (Back on Home). Set by the activity. */
    var leaveApp: () -> Unit = {}

    fun applySettings() {
        val s = AppServices.settings.current
        themeKey = s.theme
        motionEnabled = !s.reduceMotion
        canvasWidth = s.interfaceWidth.coerceIn(960, 1920)
    }

    fun start() {
        applySettings()
        // Any request answered with 401 while signed in means the session is gone.
        AppServices.jellyfin.onSessionRejected = { scope.launch { sessionExpired(null) } }
        scope.launch {
            // Saved sign-ins and the integrations' keys are decrypted in parallel, off the main thread.
            val integrations = async { AppServices.loadIntegrations() }
            AppServices.accounts.load()
            val auto = AppServices.session.autoLoginCandidate
            // Instant start: show Home (from the last visit's cache) at once, check the session meanwhile.
            // The cached Home is read and turned into cards in the background (needs the server address set first).
            if (auto != null) AppServices.session.beginResume(auto)
            val cached = auto?.let { a -> async(Dispatchers.Default) { runCatching { AppServices.loadHome(a.id)?.let { HomeModel.prepare(it) } }.getOrNull() } }
            integrations.await()
            if (auto != null) {
                showShell(cached?.await())
                isStarting = false
                validateSession(auto)
                return@launch
            }
            showAccountPicker()
            isStarting = false
        }
    }

    private suspend fun validateSession(account: SavedAccount) {
        when (AppServices.session.validate()) {
            SessionCheck.Valid -> shell?.refreshAccount()
            SessionCheck.Rejected -> sessionExpired(account)
            SessionCheck.Unreachable -> {
                Log.warn("server unreachable at startup; showing what was cached")
                toast("Can't reach ${account.serverName ?: "the server"} right now. Showing what was there last time.", 5000)
            }
        }
    }

    /** The server no longer accepts this sign-in (revoked or expired): back to the sign-in screen, once. */
    private fun sessionExpired(account: SavedAccount?) {
        if (root !is Shell) return
        Log.warn("session rejected by the server (401): signing in again")
        closePlayer()
        AppServices.socket.stop()
        val acc = account ?: AppServices.session.current
        AppServices.session.switchUser()
        if (acc != null) showLogin(acc.serverUrl, prefillUser = acc.userName, message = "Your session expired. Please sign in again.")
        else showAccountPicker()
    }

    private fun replaceRoot(vm: ViewModelBase) {
        val old = root
        root = vm
        if (old !== vm) old?.dispose()
    }

    fun showAccountPicker() {
        AppServices.socket.stop()
        if (AppServices.session.savedAccounts.isEmpty()) replaceRoot(ServerSelectModel()) else replaceRoot(AccountPickerModel())
    }

    fun showServerSelect() = replaceRoot(ServerSelectModel())

    fun showLogin(serverUrl: String, info: PublicSystemInfo? = null, prefillUser: String? = null, message: String? = null) =
        replaceRoot(LoginModel(serverUrl, info, prefillUser, message))

    fun showShell(cached: HomeModel.Prepared? = null) {
        val shell = Shell()
        replaceRoot(shell)
        shell.initialize(cached)
        // Lets other Jellyfin apps control this one, and keeps the screen in step with changes made elsewhere.
        RemoteControl.start()
        maybeAutoCheckUpdates()
    }

    private var updateChecked = false

    /** Opt-in only (Settings → Updates): one quiet check per launch, like the Windows / Linux apps. */
    private fun maybeAutoCheckUpdates() {
        val s = AppServices.settings.current
        if (updateChecked || !s.autoCheckUpdates || s.updateSource.isBlank()) return
        updateChecked = true
        AppServices.scope.launch {
            try {
                delay(8000)
                val u = io.github.devall96.nettle.tv.platform.Updater.check(s.updateSource)
                if (u != null) toast("Nettle ${u.version} is available. Install it from Settings → Updates.", 6000)
            } catch (e: kotlinx.coroutines.CancellationException) { throw e } catch (e: Exception) {
                io.github.devall96.nettle.core.services.Log.warn("auto update check failed: ${e.message}")
            }
        }
    }

    fun switchUser() {
        closePlayer()
        AppServices.socket.stop()
        AppServices.session.switchUser()
        showAccountPicker()
    }

    // ---------- Playback ----------

    fun playItem(item: BaseItemDto, resume: Boolean, mediaSourceId: String? = null) {
        closePlayer()
        val start = if (resume) item.userData?.playbackPositionTicks?.takeIf { it > 0 } else null
        player = PlayerModel.forItem(item, start, mediaSourceId)
    }

    /** Plays a list in order from [index] (Play all, Shuffle, remote control). */
    fun playQueue(items: List<BaseItemDto>, index: Int = 0, startTicks: Long? = null) {
        if (items.isEmpty()) return
        closePlayer()
        player = PlayerModel.forQueue(items, index, startTicks)
    }

    fun playLive(channels: List<NtvChannel>, index: Int) {
        closePlayer()
        player = PlayerModel.forLive(channels, index)
    }

    fun closePlayer() {
        val p = player ?: return
        player = null
        p.dispose()
        // Refresh once the server has the final position, so Continue Watching shows it.
        scope.launch {
            withTimeoutOrNull(2500) { p.stopped.await() }
            shell?.refreshCurrent()
        }
    }

    // ---------- Toasts ----------

    fun toast(text: String, ms: Long = 2600) {
        toastJob?.cancel()
        toastText = text
        toastJob = scope.launch {
            delay(ms)
            toastText = null
        }
    }
}
