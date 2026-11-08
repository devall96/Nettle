package io.github.devall96.nettle.core.api

import io.github.devall96.nettle.core.models.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.KSerializer
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException

/**
 * Seerr request-management client (port of Nettle.Core.Api.SeerrClient). Auth by API key
 * (X-Api-Key) or by signing in with the user's Jellyfin credentials, in which case only the
 * resulting session cookie is kept (encrypted, by the caller).
 */
class SeerrClient(base: OkHttpClient) {
    /** Only Seerr's own session cookie, only for the configured Seerr server. */
    private inner class SessionJar : CookieJar {
        @Volatile var cookie: String? = null
        override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
            if (!isSeerr(url)) return
            cookies.firstOrNull { it.name == "connect.sid" }?.let { cookie = it.value }
        }
        override fun loadForRequest(url: HttpUrl): List<Cookie> {
            val c = cookie ?: return emptyList()
            if (!isSeerr(url)) return emptyList()
            return listOf(Cookie.Builder().name("connect.sid").value(c).hostOnlyDomain(url.host).path("/").build())
        }
        private fun isSeerr(url: HttpUrl): Boolean {
            val b = baseUrl?.toHttpUrlOrNull() ?: return false
            return b.host.equals(url.host, true) && b.port == url.port
        }
    }

    private val jar = SessionJar()
    private val http = HttpFactory.builder(base, 20).cookieJar(jar).build()

    var baseUrl: String? = null; private set
    var apiKey: String? = null; private set
    val isConfigured: Boolean get() = baseUrl != null && (apiKey != null || sessionCookie != null)
    val sessionCookie: String? get() = if (baseUrl == null) null else jar.cookie

    /** When true, posters load straight from image.tmdb.org (a third party). Off by default. */
    @Volatile var allowDirectTmdbImages: Boolean = false

    fun configure(baseUrl: String?, apiKey: String?, sessionCookie: String?) {
        this.baseUrl = if (baseUrl.isNullOrBlank()) null else normalizeUrl(baseUrl)
        this.apiKey = apiKey?.trim()?.takeIf { it.isNotEmpty() }
        jar.cookie = sessionCookie?.takeIf { it.isNotEmpty() && this.baseUrl != null }
        me = null
    }

    companion object {
        private val JSON = "application/json; charset=utf-8".toMediaType()

        fun normalizeUrl(url: String): String {
            var u = url.trim().trimEnd('/')
            if (!u.startsWith("http", ignoreCase = true)) u = "http://$u"
            return u
        }
    }

    private fun req(method: String, path: String, body: String? = null): Request {
        val b = Request.Builder().url(baseUrl + "/api/v1" + path).header("Accept", "application/json")
        apiKey?.let { b.header("X-Api-Key", it) }
        b.method(method, body?.toRequestBody(JSON))
        return b.build()
    }

    private suspend fun <T> send(r: Request, s: KSerializer<T>): T {
        if (baseUrl == null) throw ApiException("Seerr isn't set up yet. Add it in Settings.")
        val resp = try { http.newCall(r).await() } catch (e: CancellationException) { throw e } catch (e: IOException) {
            throw ApiException("Can't reach Seerr. ${e.message ?: ""}".trim(), null, e)
        }
        return resp.use {
            if (!it.isSuccessful) throw ApiException(
                if (it.code == 401 || it.code == 403) "Seerr rejected the credentials." else "Seerr returned ${it.code}.", it.code)
            val text = withContext(Dispatchers.IO) { it.body.string() }
            try { NettleJson.camel.decodeFromString(s, text) } catch (e: Exception) { throw ApiException("Seerr sent something unexpected.", null, e) }
        }
    }

    /** True if a Seerr server answers at [url] (its public status endpoint). */
    suspend fun probe(url: String, timeoutMs: Long): Boolean = withTimeoutOrNull(timeoutMs) {
        try {
            http.newCall(Request.Builder().url(normalizeUrl(url) + "/api/v1/status").build()).await().use { r ->
                r.isSuccessful && withContext(Dispatchers.IO) { r.body.string() }.contains("\"version\"")
            }
        } catch (e: CancellationException) { throw e } catch (_: Exception) { false }
    } ?: false

    suspend fun signInWithJellyfin(url: String, username: String, password: String): SeerrUser {
        baseUrl = normalizeUrl(url)
        apiKey = null
        jar.cookie = null
        me = null
        val body = NettleJson.camel.encodeToString(SeerrJellyfinLogin.serializer(), SeerrJellyfinLogin(username, password))
        return send(req("POST", "/auth/jellyfin", body), SeerrUser.serializer())
    }

    private var me: SeerrUser? = null
    private var meFor: String? = null

    /** The Seerr account Nettle is signed in as (cached per server + credentials). */
    suspend fun getMe(): SeerrUser {
        val key = "$baseUrl|$apiKey|$sessionCookie"
        me?.let { if (meFor == key) return it }
        val m = send(req("GET", "/auth/me"), SeerrUser.serializer())
        me = m
        meFor = key
        return m
    }

    suspend fun trending(page: Int = 1) = send(req("GET", "/discover/trending" + Query().add("page", page)), SeerrPage.serializer())
    suspend fun popularMovies(page: Int = 1) = send(req("GET", "/discover/movies" + Query().add("page", page)), SeerrPage.serializer())
    suspend fun popularTv(page: Int = 1) = send(req("GET", "/discover/tv" + Query().add("page", page)), SeerrPage.serializer())
    suspend fun upcomingMovies(page: Int = 1) = send(req("GET", "/discover/movies/upcoming" + Query().add("page", page)), SeerrPage.serializer())
    suspend fun search(query: String, page: Int = 1) = send(req("GET", "/search" + Query().add("query", query).add("page", page)), SeerrPage.serializer())
    suspend fun tv(tmdbId: Int) = send(req("GET", "/tv/$tmdbId"), SeerrDetails.serializer())
    suspend fun movie(tmdbId: Int) = send(req("GET", "/movie/$tmdbId"), SeerrDetails.serializer())

    /**
     * Requests made by the signed-in Seerr user only. Seerr returns everyone's requests to
     * admins, so the list is filtered by requester on the server and checked again here.
     */
    suspend fun myRequests(take: Int = 30): List<SeerrRequest> {
        val m = getMe()
        val page = send(req("GET", "/request" + Query().add("take", take).add("skip", 0).add("sort", "added").add("requestedBy", m.id)),
            SeerrRequestPage.serializer())
        return page.results.filter { it.requestedBy?.id == m.id }
    }

    /** Requests a movie, or the given seasons of a series (null = every season not already available or requested). */
    suspend fun request(item: SeerrResult, seasons: Collection<Int>? = null): SeerrRequest {
        val type = item.mediaType ?: "movie"
        var chosen: List<Int>? = null
        if (type == "tv") {
            chosen = if (!seasons.isNullOrEmpty()) seasons.toList() else {
                val t = tv(item.id)
                val taken = t.mediaInfo?.seasons?.filter { it.status >= SeerrMediaStatus.PENDING }?.map { it.seasonNumber }?.toSet() ?: emptySet()
                t.seasons?.filter { it.seasonNumber > 0 && it.seasonNumber !in taken }?.map { it.seasonNumber } ?: emptyList()
            }
        }
        val body = NettleJson.camel.encodeToString(SeerrCreateRequest.serializer(), SeerrCreateRequest(type, item.id, chosen))
        return send(req("POST", "/request", body), SeerrRequest.serializer())
    }

    /**
     * Poster URL. By default it goes through the user's own Seerr server image proxy
     * (/imageproxy/tmdb/...), so TMDB never sees this device's address or what is being browsed.
     */
    fun imageUrl(path: String?, width: Int = 342): String? {
        if (path.isNullOrEmpty()) return null
        if (allowDirectTmdbImages) return "https://image.tmdb.org/t/p/w$width$path"
        return baseUrl?.let { "$it/imageproxy/tmdb/t/p/w$width$path" }
    }
}
