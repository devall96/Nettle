package io.github.devall96.nettle.core.services

import io.github.devall96.nettle.core.api.ApiException
import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Secret storage (Android Keystore on the TV). Port of Nettle.Core.Platform.ISecureStore. */
interface SecureStore {
    suspend fun get(key: String): String?
    suspend fun set(key: String, value: String)
    suspend fun remove(key: String)
}

/**
 * Tiny rolling diagnostic log in the app's private folder. Thread-safe, best-effort, capped at
 * ~1 MB. Never leaves the device, and no secrets are ever written to it.
 */
object Log {
    private val gate = Any()
    private var file: File? = null
    /** Also mirror to the platform log (logcat) when set. */
    var sink: ((String, String) -> Unit)? = null

    fun init(dataDirectory: File, header: String) {
        val f = File(dataDirectory, "nettle.log")
        runCatching { if (f.exists() && f.length() > 1_000_000) f.renameTo(File(dataDirectory, "nettle.log.old")) }
        file = f
        info("---- Nettle start ($header)")
    }

    fun info(msg: String) = write("INF", msg)
    fun warn(msg: String) = write("WRN", msg)
    fun error(msg: String, ex: Throwable? = null) = write("ERR", if (ex == null) msg else "$msg: $ex")

    private val secrets = Regex("(?i)(api_?key|token|x-emby-token|access_?token)([=:\"\\s]+)[A-Za-z0-9._-]{8,}")

    /** Masks access tokens / API keys anywhere in a message (including URLs from the player). */
    fun redact(text: String?): String = if (text.isNullOrEmpty()) "" else secrets.replace(text, "$1$2***")

    private val time = SimpleDateFormat("HH:mm:ss.SSS", Locale.ROOT)

    /** File writes happen on one background thread, in order, so logging never stalls the UI thread. */
    private val writer = java.util.concurrent.Executors.newSingleThreadExecutor { r -> Thread(r, "nettle-log").apply { isDaemon = true } }

    private fun write(level: String, msg: String) {
        val m = redact(msg)
        sink?.invoke(level, m)
        val f = file ?: return
        val line = synchronized(gate) { "${time.format(Date())} $level [${Thread.currentThread().id}] $m\n" }
        writer.execute { runCatching { f.appendText(line) } }
    }
}

/** Non-secret preferences (theme, playback options). Secrets never go here. */
class SettingsStore(dataDirectory: File) {
    private val path = File(dataDirectory.apply { mkdirs() }, "settings.json")
    private val gate = Mutex()

    var current: AppSettings = AppSettings(); private set
    var onChanged: (() -> Unit)? = null

    fun load() {
        current = try {
            if (path.exists()) NettleJson.files.decodeFromString(AppSettings.serializer(), path.readText()) else AppSettings()
        } catch (e: Exception) {
            // Start from defaults, but keep a copy of the old file first (the next save would overwrite it).
            backupDamagedFile(path, e)
            AppSettings()
        }
    }

    suspend fun save() {
        gate.withLock {
            withContext(Dispatchers.IO) {
                val tmp = File(path.path + ".tmp")
                tmp.writeText(NettleJson.files.encodeToString(AppSettings.serializer(), current))
                if (!tmp.renameTo(path)) { path.delete(); tmp.renameTo(path) }
            }
        }
        onChanged?.invoke()
    }

    companion object {
        /** Copies an unreadable file to "name.broken-yyyyMMdd-HHmmss" next to it. */
        fun backupDamagedFile(path: File, reason: Exception? = null): File? = runCatching {
            if (!path.exists()) return null
            val backup = File(path.path + ".broken-" + SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(Date()))
            path.copyTo(backup, overwrite = true)
            Log.warn("${path.name} couldn't be read (${reason?.message}); reset to defaults, old copy kept as ${backup.name}")
            backup
        }.getOrNull()
    }
}

/**
 * Remembered accounts. The whole vault (server URLs, user ids, access tokens) is kept in the
 * secure store. Passwords are never stored: Jellyfin issues a long-lived access token on first
 * sign-in and that token is what enables auto-login.
 */
class AccountStore(private val secure: SecureStore) {
    private val key = "accounts.v1"
    private var vault = AccountVault()

    val accounts: List<SavedAccount> get() = vault.accounts.sortedByDescending { it.lastUsed }

    suspend fun load() {
        vault = try {
            val json = secure.get(key)
            if (json.isNullOrEmpty()) AccountVault() else NettleJson.files.decodeFromString(AccountVault.serializer(), json)
        } catch (e: Exception) {
            // Never silently drop saved sign-ins: keep the unreadable copy under another key.
            runCatching {
                val raw = secure.get(key)
                if (!raw.isNullOrEmpty()) secure.set("$key.broken-" + SimpleDateFormat("yyyyMMdd-HHmmss", Locale.ROOT).format(Date()), raw)
            }
            Log.warn("saved accounts couldn't be read (${e.message}); a copy was kept")
            AccountVault()
        }
    }

    fun find(id: String?): SavedAccount? = id?.let { i -> vault.accounts.firstOrNull { it.id == i } }
    fun findByUser(serverId: String, userId: String): SavedAccount? = vault.accounts.firstOrNull { it.serverId == serverId && it.userId == userId }

    suspend fun upsert(account: SavedAccount) {
        vault.accounts.removeAll { it.id == account.id }
        vault.accounts += account
        persist()
    }

    suspend fun remove(id: String) {
        vault.accounts.removeAll { it.id == id }
        persist()
    }

    private suspend fun persist() = secure.set(key, NettleJson.files.encodeToString(AccountVault.serializer(), vault))
}

enum class SessionCheck { Valid, Rejected, Unreachable }

/** Owns "who is signed in": resuming saved accounts, completing sign-ins, switching users. */
class SessionService(private val client: JellyfinClient, private val accountStore: AccountStore, private val settings: SettingsStore) {
    var current: SavedAccount? = null; private set

    /** The signed-in user's playback preferences from their Jellyfin profile. */
    var userConfig: UserConfiguration? = null; private set

    var onSessionChanged: (() -> Unit)? = null

    /** Audio language to prefer: this device's setting if set, else the user's Jellyfin profile. */
    fun preferredAudioLanguage(deviceSetting: String?): String? =
        deviceSetting?.takeIf { it.isNotBlank() } ?: userConfig?.audioLanguagePreference?.takeIf { it.isNotBlank() }

    /** Subtitle language to prefer: this device's setting if set, else the user's Jellyfin profile. */
    fun preferredSubtitleLanguage(deviceSetting: String?): String? =
        deviceSetting?.takeIf { it.isNotBlank() } ?: userConfig?.subtitleLanguagePreference?.takeIf { it.isNotBlank() }

    /** Default, Always, OnlyForced, Smart or None: this device's setting, else the profile's, else Default. */
    fun subtitleMode(deviceSetting: String?): String =
        deviceSetting?.takeIf { it.isNotBlank() } ?: userConfig?.subtitleMode?.takeIf { it.isNotBlank() } ?: "Default"

    /** Libraries this user hid from "Recently added". */
    val latestExcludes: Collection<String> get() = userConfig?.latestItemsExcludes ?: emptyList()

    val savedAccounts: List<SavedAccount> get() = accountStore.accounts

    /** Account to sign into automatically at startup, if the user enabled auto-login. */
    val autoLoginCandidate: SavedAccount? get() = if (settings.current.autoLogin) accountStore.find(settings.current.lastAccountId) else null

    /** Validates the stored token. False if the server rejected it; throws [ApiException] when unreachable. */
    suspend fun resume(account: SavedAccount): Boolean {
        client.configure(account.serverUrl, account.deviceId, account.accessToken, account.userId)
        when (check(account)) {
            SessionCheck.Unreachable -> throw ApiException("Can't reach the server.")
            SessionCheck.Rejected -> { client.clearSession(); return false }
            SessionCheck.Valid -> {}
        }
        activate(account)
        return true
    }

    /** Instant start: uses the saved account straight away with no network round trip. Follow with [validate]. */
    fun beginResume(account: SavedAccount) {
        client.configure(account.serverUrl, account.deviceId, account.accessToken, account.userId)
        userConfig = account.userConfig
        current = account
        onSessionChanged?.invoke()
    }

    /** Second half of [beginResume]: asks the server who we are and saves what changed. */
    suspend fun validate(): SessionCheck {
        val account = current ?: return SessionCheck.Rejected
        val c = check(account)
        if (c == SessionCheck.Valid && current === account) activate(account)
        if (c == SessionCheck.Rejected && current === account) {
            current = null
            userConfig = null
            client.clearSession()
        }
        return c
    }

    private suspend fun check(account: SavedAccount): SessionCheck = try {
        val me = client.getMe()
        account.userName = me.name ?: account.userName
        account.avatarTag = me.primaryImageTag
        account.isAdmin = me.policy?.isAdministrator ?: account.isAdmin
        account.userConfig = me.configuration ?: account.userConfig
        userConfig = account.userConfig
        SessionCheck.Valid
    } catch (e: ApiException) {
        if (e.statusCode == 401) SessionCheck.Rejected else SessionCheck.Unreachable
    }

    /** Prepare the client for a fresh login against [serverUrl]. */
    fun beginLogin(serverUrl: String): String {
        val deviceId = SavedAccount.newId()
        client.configure(serverUrl, deviceId)
        return deviceId
    }

    suspend fun completeLogin(serverUrl: String, server: PublicSystemInfo, auth: AuthenticationResult, remember: Boolean): SavedAccount {
        val user = auth.user
        if (user == null || auth.accessToken.isNullOrEmpty()) throw ApiException("Sign-in failed: the server returned no session.")
        // Signing in again as an already-saved user replaces that saved entry.
        val account = accountStore.findByUser(server.id ?: "", user.id) ?: SavedAccount()
        account.serverUrl = serverUrl
        account.serverId = server.id
        account.serverName = server.serverName
        account.userId = user.id
        account.userName = user.name ?: ""
        account.avatarTag = user.primaryImageTag
        account.accessToken = auth.accessToken
        account.deviceId = client.deviceId
        account.isAdmin = user.policy?.isAdministrator ?: false
        account.userConfig = user.configuration
        userConfig = user.configuration
        if (remember) activate(account) else {
            current = account
            onSessionChanged?.invoke()
        }
        return account
    }

    private suspend fun activate(account: SavedAccount) {
        account.lastUsed = System.currentTimeMillis()
        accountStore.upsert(account)
        settings.current.lastAccountId = account.id
        settings.save()
        val changed = current !== account
        current = account
        if (changed) onSessionChanged?.invoke()
    }

    /** Leave the current user but keep them in the saved list (quick switch back later). */
    fun switchUser() {
        current = null
        userConfig = null
        client.clearSession()
        onSessionChanged?.invoke()
    }

    /** Full sign out: revoke the token on the server and forget the account on this device. */
    suspend fun signOutAndForget(account: SavedAccount) {
        if (current?.id == account.id) {
            client.logout()
            current = null
            userConfig = null
            client.clearSession()
        }
        accountStore.remove(account.id)
        if (settings.current.lastAccountId == account.id) {
            settings.current.lastAccountId = null
            settings.save()
        }
        onSessionChanged?.invoke()
    }
}
