package io.github.devall96.nettle.tv.vm

import androidx.compose.foundation.ScrollState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import io.github.devall96.nettle.core.api.SeerrClient
import io.github.devall96.nettle.core.models.FrameRateMatching
import io.github.devall96.nettle.core.models.PluginInfo
import io.github.devall96.nettle.core.models.SegmentAction
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.platform.Updater
import io.github.devall96.nettle.tv.theme.Themes
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

data class Choice<T>(val value: T, val label: String)

class PluginRow(p: PluginInfo) {
    val name = p.name ?: ""
    val version = p.version ?: ""
    val support = if (name.contains("intro", true) || name.contains("segment", true)) "Used by Nettle: skip buttons" else "Server-side"
}

/** Port of SettingsViewModel (desktop-only sections left out: hardware decoding, menu entry, display mode). */
class SettingsModel : Page() {
    override val navKey = "settings"
    private val s get() = AppServices.settings.current
    private val p get() = s.playback
    val scroll = ScrollState(0)

    val segmentChoices = listOf(
        Choice(SegmentAction.ShowButton, "Show skip button"),
        Choice(SegmentAction.AutoSkip, "Skip automatically"),
        Choice(SegmentAction.Off, "Do nothing"),
    )
    val subtitleModes = listOf(
        Choice("", "Same as my Jellyfin profile"),
        Choice("Default", "Default (the file's default track)"),
        Choice("Always", "Always on"),
        Choice("OnlyForced", "Only forced (foreign dialogue)"),
        Choice("Smart", "Smart (when audio is in another language)"),
        Choice("None", "Off"),
    )
    val frameRateChoices = listOf(
        Choice(FrameRateMatching.Off, "Off"),
        Choice(FrameRateMatching.RefreshRate, "Refresh rate"),
        Choice(FrameRateMatching.RefreshRateAndResolution, "Refresh rate and resolution"),
    )
    val interfaceSizes = listOf(Choice(1920, "Small"), Choice(1600, "Medium"), Choice(1440, "Standard (like Windows)"), Choice(1280, "Large"))

    val versionText get() = "Nettle ${AppServices.version}  •  Android TV"
    val serverText get() = "${AppServices.session.current?.serverName}  •  ${AppServices.session.current?.serverUrl}"
    val userText get() = AppServices.session.current?.userName ?: ""

    var theme by mutableStateOf(s.theme); private set
    var reduceMotion by mutableStateOf(s.reduceMotion); private set
    var interfaceWidth by mutableStateOf(s.interfaceWidth); private set
    var audioPassthrough by mutableStateOf(p.audioPassthrough); private set
    var autoPlayNext by mutableStateOf(p.autoPlayNextEpisode); private set
    var stillWatching by mutableStateOf(p.stillWatchingPrompt); private set
    var stillWatchingEpisodes by mutableStateOf(p.stillWatchingEpisodes); private set
    var introAction by mutableStateOf(p.introAction); private set
    var recapAction by mutableStateOf(p.recapAction); private set
    var creditsAction by mutableStateOf(p.creditsAction); private set
    var previewAction by mutableStateOf(p.previewAction); private set
    var commercialAction by mutableStateOf(p.commercialAction); private set
    var audioLanguage by mutableStateOf(p.preferredAudioLanguage)
    var subtitleLanguage by mutableStateOf(p.preferredSubtitleLanguage)
    var subtitleMode by mutableStateOf(p.subtitleMode); private set
    var networkBuffer by mutableStateOf(p.networkBufferSeconds); private set
    var seekStep by mutableStateOf(p.seekStepSeconds); private set
    var frameRate by mutableStateOf(p.frameRateMatching); private set
    var autoLogin by mutableStateOf(s.autoLogin); private set
    var allowDirectTmdb by mutableStateOf(s.allowDirectTmdbImages); private set
    var ntvDirectPlay by mutableStateOf(p.ntvDirectPlay); private set

    var seerrUrl by mutableStateOf(s.seerrUrl ?: "")
    var seerrApiKey by mutableStateOf("")
    var seerrUser by mutableStateOf("")
    var seerrPassword by mutableStateOf("")
    var seerrStatus by mutableStateOf<String?>(null); private set
    var ntvUrl by mutableStateOf(s.ntvUrl ?: "")
    var ntvApiKey by mutableStateOf("")
    var ntvStatus by mutableStateOf<String?>(null); private set
    val accounts = mutableStateListOf<AccountTile>()
    val plugins = mutableStateListOf<PluginRow>()
    var pluginsLoaded by mutableStateOf(false); private set

    override suspend fun load() {
        AppServices.seerrReady.join()
        seerrApiKey = AppServices.secure.get(SeerrClient.apiKeySecret(AppServices.session.current?.id)) ?: ""
        ntvApiKey = AppServices.secure.get("ntv.apikey") ?: ""
        if (AppServices.seerr.isConfigured) seerrStatus = "Connected"
        if (AppServices.ntv.isConfigured) ntvStatus = "Configured"
        accounts.clear()
        AppServices.session.savedAccounts.forEach { accounts += AccountTile(it) }
        scope.launch {
            try {
                plugins.addAll(AppServices.jellyfin.getPlugins().sortedBy { it.name }.map { PluginRow(it) })
                pluginsLoaded = true
            } catch (e: CancellationException) { throw e } catch (_: Exception) { pluginsLoaded = false } // non-admins can't list plugins
        }
    }

    private fun save() { AppServices.scope.launch { AppServices.settings.save() } }

    fun applyTheme(key: String) { theme = key; s.theme = key; Main.applySettings(); save() }
    /** Settings → Appearance → Change theme (the swatches on their own screen). */
    var isThemesOpen by mutableStateOf(false); private set
    fun openThemes() { isThemesOpen = true }
    fun closeThemes() {
        isThemesOpen = false
        AppServices.scope.launch { kotlinx.coroutines.delay(60); runCatching { requester("themes").requestFocus() } }
    }
    fun changeReduceMotion(v: Boolean) { reduceMotion = v; s.reduceMotion = v; Main.applySettings(); save() }
    fun changeInterfaceWidth(v: Int) { interfaceWidth = v; s.interfaceWidth = v; Main.applySettings(); save() }
    fun changePassthrough(v: Boolean) { audioPassthrough = v; p.audioPassthrough = v; save() }    fun changeAutoPlayNext(v: Boolean) { autoPlayNext = v; p.autoPlayNextEpisode = v; save() }
    fun changeStillWatching(v: Boolean) { stillWatching = v; p.stillWatchingPrompt = v; save() }
    fun changeStillWatchingEpisodes(v: Int) { stillWatchingEpisodes = v.coerceIn(1, 20); p.stillWatchingEpisodes = stillWatchingEpisodes; save() }
    fun changeIntro(v: SegmentAction) { introAction = v; p.introAction = v; save() }
    fun changeRecap(v: SegmentAction) { recapAction = v; p.recapAction = v; save() }
    fun changeCredits(v: SegmentAction) { creditsAction = v; p.creditsAction = v; save() }
    fun changePreview(v: SegmentAction) { previewAction = v; p.previewAction = v; save() }
    fun changeCommercial(v: SegmentAction) { commercialAction = v; p.commercialAction = v; save() }
    fun commitAudioLanguage() { p.preferredAudioLanguage = audioLanguage.trim(); save() }
    fun commitSubtitleLanguage() { p.preferredSubtitleLanguage = subtitleLanguage.trim(); save() }
    fun changeSubtitleMode(v: String) { subtitleMode = v; p.subtitleMode = v; save() }
    fun changeNetworkBuffer(v: Int) { networkBuffer = v.coerceIn(2, 300); p.networkBufferSeconds = networkBuffer; save() }
    fun changeSeekStep(v: Int) { seekStep = v.coerceIn(5, 60); p.seekStepSeconds = seekStep; save() }
    fun changeFrameRate(v: FrameRateMatching) { frameRate = v; p.frameRateMatching = v; save() }
    fun changeAutoLogin(v: Boolean) { autoLogin = v; s.autoLogin = v; save() }
    fun changeAllowDirectTmdb(v: Boolean) { allowDirectTmdb = v; s.allowDirectTmdbImages = v; AppServices.seerr.allowDirectTmdbImages = v; save() }
    fun changeNtvDirectPlay(v: Boolean) { ntvDirectPlay = v; p.ntvDirectPlay = v; save() }

    fun saveSeerr() {
        seerrStatus = "Checking…"
        scope.launch {
            try {
                val url = if (seerrUrl.isBlank()) null else SeerrClient.normalizeUrl(seerrUrl)
                s.seerrUrl = url
                AppServices.allowHostOf(url) // the user typed this address
                save()
                if (url == null) {
                    AppServices.saveSeerrSecrets(null, null)
                    AppServices.seerr.configure(null, null, null)
                    seerrStatus = "Removed"
                    return@launch
                }
                if (seerrUser.isNotBlank()) {
                    AppServices.seerr.signInWithJellyfin(url, seerrUser.trim(), seerrPassword)
                    seerrPassword = ""
                    AppServices.saveSeerrSecrets(null, AppServices.seerr.sessionCookie)
                    seerrApiKey = ""
                } else {
                    AppServices.saveSeerrSecrets(seerrApiKey.trim(), null)
                    AppServices.seerr.configure(url, seerrApiKey, null)
                }
                val me = AppServices.seerr.getMe()
                seerrStatus = "Connected as ${me.displayName ?: me.username ?: me.jellyfinUsername}"
            } catch (e: CancellationException) { throw e } catch (e: Exception) { seerrStatus = "Failed: ${e.message}" }
        }
    }

    fun saveNtv() {
        ntvStatus = "Checking…"
        scope.launch {
            try {
                s.ntvUrl = ntvUrl.trim().ifEmpty { null }
                save()
                if (ntvApiKey.isBlank()) AppServices.secure.remove("ntv.apikey") else AppServices.secure.set("ntv.apikey", ntvApiKey.trim())
                AppServices.ntv.configure(s.ntvUrl, ntvApiKey)
                AppServices.allowHostOf(AppServices.ntv.baseUrl)
                if (!AppServices.ntv.isConfigured) { ntvStatus = "Removed"; return@launch }
                val ch = AppServices.ntv.loadGuide(14, force = true)
                ntvStatus = "Connected: ${ch.size} channels, ${"%,d".format(ch.sumOf { it.programs.size })} listings"
            } catch (e: CancellationException) { throw e } catch (e: Exception) { ntvStatus = "Failed: ${e.message}" }
        }
    }

    fun switchAccount(t: AccountTile) {
        scope.launch {
            if (AppServices.session.resume(t.account)) Main.showShell()
            else Main.showLogin(t.account.serverUrl, prefillUser = t.account.userName, message = "Your session expired. Please sign in again.")
        }
    }

    fun forgetAccount(t: AccountTile) {
        scope.launch {
            val wasCurrent = AppServices.session.current?.id == t.account.id
            AppServices.session.signOutAndForget(t.account)
            AppServices.forgetAccountData(t.account.id)
            accounts.remove(t)
            if (wasCurrent) Main.showAccountPicker()
        }
    }

    fun switchUser() = Main.switchUser()

    // ---------- Updates (same source and toggle as the Windows / Linux apps) ----------

    var updateSource by mutableStateOf(s.updateSource)
    var autoCheckUpdates by mutableStateOf(s.autoCheckUpdates); private set
    var updateStatus by mutableStateOf<String?>(null); private set
    var isUpdating by mutableStateOf(false); private set
    var availableUpdate by mutableStateOf<io.github.devall96.nettle.core.services.AvailableUpdate?>(null); private set
    val installKind get() = "Installed version ${AppServices.version}"

    fun commitUpdateSource() { s.updateSource = updateSource.trim(); save() }
    fun changeAutoCheckUpdates(v: Boolean) { autoCheckUpdates = v; s.autoCheckUpdates = v; save() }

    fun checkUpdates() {
        commitUpdateSource()
        if (s.updateSource.isBlank()) { updateStatus = "Enter an update source first (Nettle's own is ${io.github.devall96.nettle.core.models.AppSettings.OFFICIAL_UPDATE_SOURCE})."; return }
        isUpdating = true
        availableUpdate = null
        updateStatus = "Checking…"
        scope.launch {
            try {
                val u = Updater.check(s.updateSource)
                availableUpdate = u
                updateStatus = if (u == null) "You're up to date (${AppServices.version})." else "Version ${u.version} is available."
            } catch (e: CancellationException) { throw e } catch (e: Exception) {
                updateStatus = "Couldn't check: ${e.message}"
            } finally { isUpdating = false }
        }
    }

    /** Download, then Android's own "update this app?" prompt. Settings and sign-ins are kept. */
    fun installUpdate() {
        val u = availableUpdate ?: return
        if (!Updater.canInstall()) {
            updateStatus = if (Updater.openInstallPermission())
                "Turn on \"Allow from this source\" for Nettle, then come back and choose Install again."
            else "Allow Nettle to install apps in the TV's settings (Apps → Security → Install unknown apps), then try again."
            return
        }
        isUpdating = true
        updateStatus = "Downloading ${u.version}…"
        scope.launch {
            try {
                val apk = Updater.download(u) { f -> updateStatus = "Downloading ${u.version}… ${(f * 100).toInt()} %" }
                updateStatus = if (Updater.install(apk)) "Confirm the update on the TV's install screen. Nettle restarts as ${u.version}."
                else "This TV couldn't open its installer. Install Nettle-AndroidTV.apk with Downloader instead."
            } catch (e: CancellationException) { throw e } catch (e: Exception) {
                updateStatus = "Update failed: ${e.message}"
            } finally { isUpdating = false }
        }
    }

    fun clearImageCache() {
        AppServices.images.diskCache?.clear()
        AppServices.images.memoryCache?.clear()
        Main.toast("Image cache cleared")
    }

    val themes get() = Themes.all
}
