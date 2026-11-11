package io.github.devall96.nettle.tv.vm

import android.content.Context
import android.net.wifi.WifiManager
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.api.NetworkSafety
import io.github.devall96.nettle.core.models.*
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.tv.AppServices
import kotlinx.coroutines.*
import java.net.URI

// ===================== "Who's watching?" =====================

class AccountTile(val account: SavedAccount) {
    val name get() = account.userName
    val server get() = account.serverName ?: runCatching { URI(account.serverUrl).host }.getOrNull() ?: account.serverUrl
    val initial get() = account.userName.take(1).uppercase().ifEmpty { "?" }
    val avatarUrl get() = AppServices.jellyfin.userImageUrl(account.userId, account.avatarTag, 240, account.serverUrl)
    var isSigningIn by mutableStateOf(false)
}

class AccountPickerModel : ViewModelBase() {
    val accounts = mutableStateListOf<AccountTile>().apply { AppServices.session.savedAccounts.forEach { add(AccountTile(it)) } }
    var isEditing by mutableStateOf(false)
    var autoLogin by mutableStateOf(AppServices.settings.current.autoLogin)
        private set

    fun changeAutoLogin(v: Boolean) {
        autoLogin = v
        AppServices.settings.current.autoLogin = v
        scope.launch { AppServices.settings.save() }
    }

    fun signIn(tile: AccountTile) {
        if (isBusy) return
        tile.isSigningIn = true
        scope.launch {
            var ok = false
            run { ok = AppServices.session.resume(tile.account) }
            tile.isSigningIn = false
            if (ok) Main.showShell()
            else if (error == null) Main.showLogin(tile.account.serverUrl, prefillUser = tile.account.userName, message = "Your session expired. Please sign in again.")
        }
    }

    fun forget(tile: AccountTile) {
        scope.launch {
            val wasCurrent = AppServices.session.current?.id == tile.account.id
            AppServices.session.signOutAndForget(tile.account)
            AppServices.forgetAccountData(tile.account.id)
            accounts.remove(tile)
            if (wasCurrent) Main.showAccountPicker()
            else if (accounts.isEmpty() && AppServices.session.current == null) Main.showServerSelect()
        }
    }

    fun addAccount() = Main.showServerSelect()
    fun toggleEdit() { isEditing = !isEditing }
}

// ===================== Server selection =====================

class ServerSelectModel : ViewModelBase() {
    var address by mutableStateOf("")
    var isScanning by mutableStateOf(false)
    val discovered = mutableStateListOf<DiscoveredServer>()
    val canGoBack get() = AppServices.session.savedAccounts.isNotEmpty()
    var isHttps by mutableStateOf(AppServices.settings.current.serverScheme == "https")
        private set

    init { scan() }

    fun scan() {
        scope.launch {
            isScanning = true
            discovered.clear()
            // Android drops broadcast replies unless a multicast lock is held; only while looking.
            val wifi = AppServices.context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
            val lock = runCatching { wifi?.createMulticastLock("nettle-discovery")?.apply { setReferenceCounted(false); acquire() } }.getOrNull()
            try {
                val found = JellyfinClient.discover(3000)
                found.forEach { AppServices.allowHostOf(it.address) }
                discovered.addAll(found)
            } finally {
                runCatching { lock?.release() }
                isScanning = false
            }
        }
    }

    fun useHttps(v: Boolean) {
        isHttps = v
        AppServices.settings.current.serverScheme = if (v) "https" else "http"
        scope.launch { AppServices.settings.save() }
    }

    val portHint: String
        get() = if (isHttps) "Port 8920 is added for you (or 443 for a name like jellyfin.example.com). Type :port to use another."
        else "Port 8096 is added for you. Type :port to use another."

    fun connect() = connectTo(address, AppServices.settings.current.serverScheme)

    fun connectTo(addr: String, scheme: String? = null) {
        if (addr.isBlank()) { error = "Enter your server's IP address, e.g. 192.168.1.20"; return }
        // The user typed this address: it may be contacted.
        val typed = addr.trim()
        AppServices.allowHostOf(if (typed.contains("://")) typed else "http://$typed")
        launch {
            val (url, info) = AppServices.jellyfin.probeServer(typed, scheme)
            Main.showLogin(url, info)
        }
    }

    fun back() = Main.showAccountPicker()
}

// ===================== Sign in =====================

class PublicUser(val user: UserDto, serverUrl: String) {
    val name = user.name ?: ""
    val initial = name.take(1).uppercase().ifEmpty { "?" }
    val avatarUrl = AppServices.jellyfin.userImageUrl(user.id, user.primaryImageTag, 240, serverUrl)
}

class LoginModel(private val serverUrl: String, private var info: PublicSystemInfo?, prefillUser: String?, message: String?) : ViewModelBase() {
    private var qcJob: Job? = null

    val users = mutableStateListOf<PublicUser>()
    var serverName by mutableStateOf(info?.serverName ?: runCatching { URI(serverUrl).host }.getOrNull() ?: serverUrl)
    var serverDetail by mutableStateOf(detail())
    val isInsecureConnection = NetworkSafety.isInsecureRemote(serverUrl)

    var username by mutableStateOf(prefillUser ?: "")
    var password by mutableStateOf("")
    var rememberMe by mutableStateOf(true)
    var showManualForm by mutableStateOf(prefillUser != null)
    var message by mutableStateOf(message)
    var quickConnectAvailable by mutableStateOf(false)
    var quickConnectCode by mutableStateOf<String?>(null)
    /** Bumped to move focus to the password box (after picking a user that has a password). */
    var passwordFocusRequests by mutableStateOf(0)

    val hasUsers get() = users.isNotEmpty()

    private fun detail() = serverUrl + (info?.version?.let { "  •  Jellyfin $it" } ?: "")

    init {
        AppServices.allowHostOf(serverUrl)
        AppServices.session.beginLogin(serverUrl)
        launch {
            if (info == null) {
                info = AppServices.jellyfin.probeServer(serverUrl).second
                serverName = info?.serverName ?: serverName
                serverDetail = detail()
            }
            coroutineScope {
                val usersD = async { runCatching { AppServices.jellyfin.getPublicUsers() }.getOrDefault(emptyList()) }
                val qcD = async { AppServices.jellyfin.isQuickConnectEnabled() }
                usersD.await().forEach { users += PublicUser(it, serverUrl) }
                quickConnectAvailable = qcD.await()
            }
            if (users.isEmpty()) showManualForm = true
        }
    }

    fun selectUser(u: PublicUser) {
        username = u.name
        password = ""
        showManualForm = true
        if (!u.user.hasPassword) signIn() else passwordFocusRequests++
    }

    fun signIn() {
        if (username.isBlank()) { error = "Enter your username."; return }
        // A Quick Connect code still waiting would otherwise sign in a second time if approved later.
        cancelQuickConnect()
        launch {
            val user = username.trim()
            val pw = password
            val auth = AppServices.jellyfin.authenticate(user, pw)
            password = ""
            finish(auth, user, pw)
        }
    }

    fun startQuickConnect() {
        qcJob?.cancel()
        qcJob = launch(showBusy = false) {
            val start = AppServices.jellyfin.quickConnectInitiate()
            quickConnectCode = start.code
            val secret = start.secret ?: return@launch
            val deadline = System.currentTimeMillis() + 10 * 60_000
            while (System.currentTimeMillis() < deadline) {
                delay(2500)
                val state = AppServices.jellyfin.quickConnectState(secret)
                if (state.authenticated) {
                    val auth = AppServices.jellyfin.quickConnectAuthenticate(secret)
                    quickConnectCode = null
                    finish(auth)
                    return@launch
                }
            }
            quickConnectCode = null
        }
    }

    fun cancelQuickConnect() {
        qcJob?.cancel()
        quickConnectCode = null
    }

    private suspend fun finish(auth: AuthenticationResult, user: String? = null, password: String? = null) {
        val i = (info ?: PublicSystemInfo(id = auth.serverId, serverName = serverName)).let { if (it.id == null) it.copy(id = auth.serverId) else it }
        info = i
        AppServices.session.completeLogin(serverUrl, i, auth, rememberMe)
        Main.showShell()
        // Seerr and NostalgiaTV on the same machine get set up in the background.
        AppServices.scope.launch { IntegrationAutoSetup.run(serverUrl, user ?: auth.user?.name, password) }
    }

    fun showManual() { showManualForm = true; username = "" }
    fun showUsers() { showManualForm = false }
    fun changeServer() { qcJob?.cancel(); Main.showServerSelect() }
}

/**
 * After a Jellyfin sign-in, looks for Seerr (port 5055) and NostalgiaTV (port 19850) on the same
 * machine as the Jellyfin server, with the same http / https, and sets up whichever answers
 * (8 s each: a first connection over Tailscale from a TV box can be slow).
 * Seerr is signed in with the Jellyfin username and password just entered; the password is only
 * passed on, never stored. Anything already set up is left alone. Only the Jellyfin server's own
 * host is ever contacted.
 */
object IntegrationAutoSetup {
    const val SEERR_PORT = 5055
    const val NTV_PORT = 19850

    suspend fun run(jellyfinUrl: String, username: String?, password: String?) {
        try {
            val jf = runCatching { URI(jellyfinUrl) }.getOrNull() ?: return
            val s = AppServices.settings.current
            val host = if (jf.host.contains(':') && !jf.host.startsWith("[")) "[${jf.host}]" else jf.host
            // Same scheme as Jellyfin first; https setups often still run these add-ons on plain http.
            val schemes = if (jf.scheme == "https") listOf("https", "http") else listOf("http")
            val done = ArrayList<String>()
            var changed = false

            if (!AppServices.ntv.isConfigured && s.ntvUrl.isNullOrBlank()) {
                for (scheme in schemes) {
                    val url = "$scheme://$host:$NTV_PORT"
                    if (!AppServices.ntv.probe(url, 8000)) continue
                    s.ntvUrl = url
                    AppServices.ntv.configure(url, null)
                    changed = true
                    done += "Live TV"
                    Log.info("auto-setup: NostalgiaTV found on the Jellyfin host")
                    break
                }
            }

            AppServices.seerrReady.join() // this profile's own Seerr sign-in, if it has one
            if (!AppServices.seerr.isConfigured) {
                for (scheme in schemes) {
                    val url = "$scheme://$host:$SEERR_PORT"
                    if (!AppServices.seerr.probe(url, 8000)) continue
                    s.seerrUrl = url // remembered even if signing in fails, so Settings only needs a login
                    changed = true
                    // The password only goes where it's no less protected than it was for Jellyfin.
                    val downgrade = jf.scheme == "https" && scheme == "http"
                    val insecure = NetworkSafety.isInsecureRemote(url)
                    if ((downgrade || insecure) && password != null) Log.info("auto-setup: Seerr found over plain http; not sending the password there (sign in from Settings)")
                    else if (!username.isNullOrEmpty() && password != null) {
                        try {
                            withTimeout(15_000) { AppServices.seerr.signInWithJellyfin(url, username, password) }
                            AppServices.saveSeerrSecrets(null, AppServices.seerr.sessionCookie)
                            done += "Requests"
                            Log.info("auto-setup: signed in to Seerr on the Jellyfin host")
                        } catch (e: CancellationException) {
                            throw e
                        } catch (e: Exception) {
                            AppServices.seerr.configure(null, null, null)
                            Log.info("auto-setup: Seerr found but sign-in failed: ${e.message}")
                        }
                    } else Log.info("auto-setup: Seerr found; no password to sign in with")
                    break
                }
            }
            if (changed) AppServices.settings.save()
            if (done.isNotEmpty()) Main.toast("Set up ${done.joinToString(" and ")} automatically", 4000)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.warn("auto-setup failed: ${e.message}")
        }
    }
}
