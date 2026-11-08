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
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

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

    /** Loads secrets for optional integrations (Seerr / NostalgiaTV). */
    suspend fun loadIntegrations() {
        val s = settings.current
        seerr.configure(s.seerrUrl, secure.get("seerr.apikey"), secure.get("seerr.cookie"))
        seerr.allowDirectTmdbImages = s.allowDirectTmdbImages
        ntv.configure(s.ntvUrl, secure.get("ntv.apikey"))
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
