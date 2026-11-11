package io.github.devall96.nettle.tv

import android.content.Context
import coil3.ImageLoader
import coil3.disk.DiskCache
import coil3.memory.MemoryCache
import coil3.network.okhttp.OkHttpNetworkFetcherFactory
import coil3.request.crossfade
import io.github.devall96.nettle.core.api.*
import io.github.devall96.nettle.core.models.HomeSnapshot
import io.github.devall96.nettle.core.models.NettleJson
import io.github.devall96.nettle.core.services.*
import io.github.devall96.nettle.tv.platform.KeystoreSecureStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.CancellationException
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okio.Path.Companion.toOkioPath
import java.io.File
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap

/** Everything shared across screens (port of Nettle.UI.AppServices). */
object AppServices {
    const val DEVICE_NAME = "Nettle (Android TV)"

    lateinit var context: Context; private set
    lateinit var settings: SettingsStore; private set
    lateinit var secure: SecureStore; private set
    lateinit var accounts: AccountStore; private set
    lateinit var session: SessionService; private set
    /** Watch together (Jellyfin SyncPlay). */
    lateinit var syncPlay: SyncPlayService; private set
    lateinit var http: OkHttpClient; private set
    lateinit var jellyfin: JellyfinClient; private set
    lateinit var seerr: SeerrClient; private set
    lateinit var ntv: NtvClient; private set
    lateinit var segments: SegmentService; private set
    lateinit var ntvResolver: NtvItemResolver; private set
    lateinit var socket: JellyfinSocket; private set
    lateinit var images: ImageLoader; private set

    /** Whether the TV accepts Dolby Vision (set from the display's HDR capabilities at start). */
    @Volatile var displaySupportsDolbyVision = true

    /** HDR types the TV reports it accepts (Display.getHdrCapabilities), set by the activity. */
    data class HdrCaps(val hdr10: Boolean = false, val hlg: Boolean = false, val hdr10Plus: Boolean = false, val dolbyVision: Boolean = false)
    @Volatile var hdrTypes = HdrCaps()

    /** App-wide scope for work that outlives a screen (reports, saves). */
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate + io.github.devall96.nettle.tv.vm.ServerErrorNet.handler)

    val version: String get() = BuildConfig.VERSION_NAME

    /**
     * Hosts the user pointed Nettle at that aren't saved yet: addresses being tried on the
     * Connect screen, servers found on the LAN, NostalgiaTV's channel stream hosts.
     */
    private val extraHosts = ConcurrentHashMap.newKeySet<String>()

    fun allowHostOf(url: String?) {
        val h = url?.let { runCatching { java.net.URI(it.trim()).host }.getOrNull() } ?: return
        extraHosts += h.lowercase().trim('[', ']')
    }

    private fun hostOf(url: String?): String? = url?.let { runCatching { java.net.URI(it).host?.lowercase()?.trim('[', ']') }.getOrNull() }

    /**
     * The only hosts Nettle will ever fetch images from: servers the user configured (Jellyfin,
     * saved accounts' servers, NostalgiaTV, Seerr), plus TMDB only if opted in.
     */
    fun isAllowedImageHost(host: String): Boolean {
        val h = host.lowercase()
        if (h == hostOf(jellyfin.baseUrl) || h == hostOf(ntv.baseUrl) || h == hostOf(seerr.baseUrl)) return true
        if (session.savedAccounts.any { hostOf(it.serverUrl) == h }) return true
        if (settings.current.allowDirectTmdbImages && h == "image.tmdb.org") return true
        return false
    }

    /** Any network request at all: configured servers, plus the hosts in [extraHosts]. */
    fun isAllowedHost(host: String): Boolean = isAllowedImageHost(host) || host.lowercase() in extraHosts

    private class Blocked(host: String) : IOException("Nettle only connects to servers you set up (blocked: $host)")

    fun init(ctx: Context) {
        context = ctx.applicationContext
        Log.init(File(context.filesDir, "logs").apply { mkdirs() }, "Android ${android.os.Build.VERSION.RELEASE}, ${android.os.Build.MODEL}")
        Log.sink = { level, msg -> android.util.Log.println(if (level == "ERR") android.util.Log.ERROR else android.util.Log.INFO, "Nettle", msg) }
        settings = SettingsStore(context.filesDir).apply { load() }
        secure = KeystoreSecureStore(context)
        val allowlist = Interceptor { chain ->
            val host = chain.request().url.host
            if (!isAllowedHost(host)) {
                Log.warn("blocked a request to $host (not a server you set up)")
                throw Blocked(host)
            }
            chain.proceed(chain.request())
        }
        http = HttpFactory.builder(null)
            .addInterceptor(allowlist)
            .build()
        jellyfin = JellyfinClient(http, DEVICE_NAME, version)
        seerr = SeerrClient(http)
        ntv = NtvClient(http)
        accounts = AccountStore(secure)
        session = SessionService(jellyfin, accounts, settings)
        segments = SegmentService(jellyfin)
        ntvResolver = NtvItemResolver(jellyfin)
        socket = JellyfinSocket(jellyfin, http)
        syncPlay = SyncPlayService(jellyfin, scope)
        io.github.devall96.nettle.tv.vm.WatchTogether.attach(syncPlay)
        // Seerr sign-ins (and a watch-together group) belong to a profile: follow whoever is signed in.
        session.onSessionChanged = {
            val id = session.current?.id
            seerrReady = scope.launch { loadSeerrFor(id) }
            syncPlay.reset()
        }

        // Images: stricter allowlist, and the token header only for the signed-in Jellyfin server.
        val imageHttp = http.newBuilder().addInterceptor { chain ->
            val r = chain.request()
            if (!isAllowedImageHost(r.url.host)) throw Blocked(r.url.host)
            val token = jellyfin.tokenHeaderValue
            chain.proceed(if (token != null && jellyfin.isServerUrl(r.url.toString())) r.newBuilder().header("Authorization", token).build() else r)
        }.build()
        images = ImageLoader.Builder(context)
            .components { add(OkHttpNetworkFetcherFactory(callFactory = { imageHttp })) }
            .memoryCache { MemoryCache.Builder().maxSizePercent(context, 0.20).build() }
            .diskCache { DiskCache.Builder().directory(File(context.cacheDir, "images").toOkioPath()).maxSizeBytes(400L * 1024 * 1024).build() }
            .crossfade(150) // 0.15 s on TV (desktop 0.35 s): half the redraws per new image
            .build()
    }

    /**
     * For Media3: the same allowlisted client, but without a limit on the whole call. The API client's 30 s call
     * timeout would cut every video download after 30 s (Media3 then silently reconnects; a live feed restarts).
     * A stalled read still times out after 30 s.
     */
    val mediaHttp: OkHttpClient by lazy {
        http.newBuilder().callTimeout(java.time.Duration.ZERO).readTimeout(java.time.Duration.ofSeconds(30)).build()
    }

    /** Loads secrets for optional integrations (NostalgiaTV; Seerr follows the signed-in profile, see [loadSeerrFor]). */
    suspend fun loadIntegrations() {
        val s = settings.current
        if (seerr.baseUrl == null) seerr.configure(s.seerrUrl, null, null)
        seerr.allowDirectTmdbImages = s.allowDirectTmdbImages
        ntv.configure(s.ntvUrl, secure.get("ntv.apikey"))
    }

    /** Finishes when the signed-in profile's Seerr sign-in has been loaded (await before using Seerr). */
    @Volatile var seerrReady: Job = Job().apply { complete() }; private set

    /**
     * Points the Seerr client at the household's Seerr server with THIS profile's own sign-in (none = not set up
     * for this profile). A sign-in saved by an older version, shared by everyone, is handed to the first profile
     * that loads it (normally the one that set it up) and the shared copy is deleted.
     */
    private suspend fun loadSeerrFor(accountId: String?) {
        try {
            var key: String? = null
            var cookie: String? = null
            if (!accountId.isNullOrEmpty()) {
                val keyName = SeerrClient.apiKeySecret(accountId)
                val cookieName = SeerrClient.cookieSecret(accountId)
                key = secure.get(keyName)
                cookie = secure.get(cookieName)
                if (key == null && cookie == null) {
                    val legacyKey = secure.get(SeerrClient.LEGACY_API_KEY)
                    val legacyCookie = secure.get(SeerrClient.LEGACY_COOKIE)
                    if (legacyKey != null || legacyCookie != null) {
                        legacyKey?.let { secure.set(keyName, it) }
                        legacyCookie?.let { secure.set(cookieName, it) }
                        secure.remove(SeerrClient.LEGACY_API_KEY)
                        secure.remove(SeerrClient.LEGACY_COOKIE)
                        key = legacyKey
                        cookie = legacyCookie
                        Log.info("Seerr: the shared sign-in now belongs to this profile only")
                    }
                }
            }
            if (session.current?.id != accountId) return // switched profile meanwhile
            seerr.configure(settings.current.seerrUrl, key, cookie)
            seerr.allowDirectTmdbImages = settings.current.allowDirectTmdbImages
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            Log.warn("Seerr sign-in couldn't be loaded: ${e.message}")
        }
    }

    /** Saves the signed-in profile's Seerr sign-in (cookie OR API key; the other is removed). */
    suspend fun saveSeerrSecrets(apiKey: String?, cookie: String?) {
        val id = session.current?.id
        if (apiKey.isNullOrEmpty()) secure.remove(SeerrClient.apiKeySecret(id)) else secure.set(SeerrClient.apiKeySecret(id), apiKey)
        if (cookie.isNullOrEmpty()) secure.remove(SeerrClient.cookieSecret(id)) else secure.set(SeerrClient.cookieSecret(id), cookie)
    }

    /** A profile was signed out and forgotten: drop what this device kept for it. */
    suspend fun forgetAccountData(accountId: String) {
        forgetHome(accountId)
        try {
            secure.remove(SeerrClient.apiKeySecret(accountId))
            secure.remove(SeerrClient.cookieSecret(accountId))
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            Log.warn("couldn't remove a forgotten profile's Seerr sign-in: ${e.message}")
        }
    }

    // ---- Home cache: the last Home this account saw, painted at once on the next start ----

    private fun homeFile(accountId: String) = File(context.cacheDir, "home-$accountId.json")

    fun loadHome(accountId: String?): HomeSnapshot? = accountId?.let { id ->
        runCatching { NettleJson.files.decodeFromString(HomeSnapshot.serializer(), homeFile(id).readText()) }.getOrNull()
    }

    fun saveHome(accountId: String?, snapshot: HomeSnapshot) {
        val id = accountId ?: return
        runCatching { homeFile(id).writeText(NettleJson.camel.encodeToString(HomeSnapshot.serializer(), snapshot)) }
    }

    fun forgetHome(accountId: String) { homeFile(accountId).delete() }
}
