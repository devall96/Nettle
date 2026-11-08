package io.github.devall96.nettle.core.api

import io.github.devall96.nettle.core.models.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerializationException
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.builtins.MapSerializer
import kotlinx.serialization.builtins.serializer
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.net.URI
import java.util.UUID

/**
 * Lean Jellyfin REST client (10.9+ routes), a port of Nettle.Core.Api.JellyfinClient.
 * Playback is ALWAYS direct: Nettle never requests HLS / transcoded streams.
 */
class JellyfinClient(private val http: OkHttpClient, private val deviceName: String, private val version: String) {

    companion object {
        const val CLIENT_NAME = "Nettle"
        /** For rows that feed the home banner (it shows the overview and genres). */
        const val LIST_FIELDS = "PrimaryImageAspectRatio,Overview,Genres,MediaSourceCount"
        /** For cards (grids, rows, search): no overview / genre text, which cards never show. */
        const val CARD_FIELDS = "PrimaryImageAspectRatio,MediaSourceCount"
        /** Episode lists show each episode's overview, but never need its media streams. */
        const val EPISODE_FIELDS = "PrimaryImageAspectRatio,Overview"
        const val DETAIL_FIELDS = "Overview,Genres,People,Studios,Taglines,MediaSources,MediaStreams,OriginalTitle,PrimaryImageAspectRatio,Chapters"

        private val JSON = "application/json; charset=utf-8".toMediaType()

        /**
         * Addresses to try for a host typed without a scheme. No port typed: an IP address most likely
         * runs Jellyfin on its default port; a name (e.g. behind a reverse proxy) most likely on 80 / 443.
         */
        fun candidatesFor(scheme: String, input: String): List<String> {
            val bare = "$scheme://$input"
            val u = runCatching { URI(bare) }.getOrNull()
            if (u?.host == null) return listOf(bare)
            val hasPort = input.contains("]:") || (!input.startsWith('[') && input.contains(':'))
            if (hasPort) return listOf(bare)
            val withPort = bare + if (scheme == "https") ":8920" else ":8096"
            val isIp = NetworkSafety.parseIpLiteral(u.host.trim('[', ']')) != null
            return if (isIp) listOf(withPort, bare) else listOf(bare, withPort)
        }

        /** LAN auto-discovery (UDP 7359), same mechanism the official clients use. */
        suspend fun discover(windowMs: Long): List<DiscoveredServer> = withContext(Dispatchers.IO) {
            val found = LinkedHashMap<String, DiscoveredServer>()
            runCatching {
                DatagramSocket().use { udp ->
                    udp.broadcast = true
                    val msg = "who is JellyfinServer?".toByteArray()
                    udp.send(DatagramPacket(msg, msg.size, InetAddress.getByName("255.255.255.255"), 7359))
                    val end = System.currentTimeMillis() + windowMs
                    val buf = ByteArray(4096)
                    while (true) {
                        val left = end - System.currentTimeMillis()
                        if (left <= 0) break
                        udp.soTimeout = left.toInt().coerceAtLeast(1)
                        val p = DatagramPacket(buf, buf.size)
                        try { udp.receive(p) } catch (_: SocketTimeoutException) { break }
                        runCatching {
                            val s = NettleJson.jellyfin.decodeFromString(DiscoveredServer.serializer(), String(p.data, 0, p.length))
                            if (s.id != null && s.address != null) found[s.id] = s
                        }
                    }
                }
            }
            found.values.toList()
        }
    }

    /**
     * Called (on a background thread) when the server rejects the saved session with 401, i.e.
     * the token was revoked or expired. Not called for sign-in attempts.
     */
    var onSessionRejected: (() -> Unit)? = null

    var baseUrl: String? = null; private set
    var token: String? = null; private set
    var userId: String? = null; private set
    var deviceId: String = UUID.randomUUID().toString().replace("-", ""); private set

    fun configure(baseUrl: String, deviceId: String, token: String? = null, userId: String? = null) {
        this.baseUrl = baseUrl.trimEnd('/')
        this.deviceId = deviceId
        this.token = token
        this.userId = userId
    }

    fun clearSession() { token = null; userId = null }

    private fun authHeader(): String {
        fun esc(s: String) = s.replace("\"", "")
        val sb = StringBuilder("MediaBrowser ")
        sb.append("Client=\"$CLIENT_NAME\", Device=\"${esc(deviceName)}\", DeviceId=\"$deviceId\", Version=\"$version\"")
        if (!token.isNullOrEmpty()) sb.append(", Token=\"$token\"")
        return sb.toString()
    }

    /** The full client identity + token, for the WebSocket handshake. */
    val authorizationHeaderValue: String get() = authHeader()

    /**
     * "Authorization" header value that carries only the token. Lets media URLs stay token-free,
     * so the token never lands in reverse-proxy access logs. Null when signed out.
     */
    val tokenHeaderValue: String? get() = if (token.isNullOrEmpty()) null else "MediaBrowser Token=\"$token\""

    /** True when [url] points at the signed-in Jellyfin server. */
    fun isServerUrl(url: String?): Boolean {
        val b = baseUrl ?: return false
        val u = runCatching { URI(url ?: return false) }.getOrNull() ?: return false
        val bu = runCatching { URI(b) }.getOrNull() ?: return false
        return u.host.equals(bu.host, ignoreCase = true) && port(u) == port(bu) && u.scheme == bu.scheme
    }

    private fun port(u: URI) = if (u.port != -1) u.port else if (u.scheme == "https") 443 else 80

    private fun req(method: String, path: String, body: RequestBody? = null, base: String? = baseUrl): Request =
        Request.Builder().url(base + path)
            .header("Authorization", authHeader())
            .header("Accept", "application/json")
            .method(method, body ?: if (method == "POST") ByteArray(0).toRequestBody(null) else null)
            .build()

    private fun <T> json(value: T, s: KSerializer<T>): RequestBody = NettleJson.jellyfin.encodeToString(s, value).toRequestBody(JSON)

    private suspend fun <T> send(r: Request, s: KSerializer<T>): T {
        val body = sendRaw(r)
        return try { NettleJson.jellyfin.decodeFromString(s, body) } catch (e: SerializationException) {
            throw ApiException("The server sent something unexpected.", null, e)
        }
    }

    private suspend fun sendRaw(r: Request): String {
        val resp = try { http.newCall(r).await() } catch (e: CancellationException) { throw e } catch (e: SocketTimeoutException) {
            throw ApiException("The server took too long to respond.", null, e)
        } catch (e: IOException) {
            throw ApiException("Can't reach the server. ${e.message ?: ""}".trim(), null, e)
        }
        return resp.use {
            checkRejected(r, it.code)
            val text = withContext(Dispatchers.IO) { it.body.string() }
            if (!it.isSuccessful) {
                var msg = when (it.code) {
                    401 -> "Your session has expired or the credentials are wrong."
                    403 -> "You don't have permission to do that."
                    404 -> "Not found on the server."
                    else -> "Server returned ${it.code} ${it.message}.".replace(" .", ".")
                }
                if (text.isNotBlank() && text.length < 300 && !text.trimStart().startsWith('<')) msg += " " + text.trim()
                throw ApiException(msg, it.code)
            }
            text
        }
    }

    /** A 401 on a request made with our token means the session is gone (not a wrong password). */
    private fun checkRejected(r: Request, code: Int) {
        if (code != 401 || token.isNullOrEmpty()) return
        val path = r.url.encodedPath
        if (path.contains("/Authenticate", true) || path.endsWith("/Sessions/Logout", true)) return
        runCatching { onSessionRejected?.invoke() }
    }

    // ---------- Server / auth ----------

    /**
     * Normalizes user input ("myserver", "192.168.1.5:8096", "https://jf.example.com/") and probes it.
     * With [scheme] ("http" / "https") and no scheme typed, only that scheme is tried, with
     * Jellyfin's default port for it (8096 / 8920) unless a port was typed.
     */
    suspend fun probeServer(input: String, scheme: String? = null): Pair<String, PublicSystemInfo> = coroutineScope {
        val i = input.trim().trimEnd('/')
        val candidates = mutableListOf<String>()
        if (i.startsWith("http://", true) || i.startsWith("https://", true)) candidates += i
        else if (scheme == "http" || scheme == "https") candidates += candidatesFor(scheme, i)
        else {
            val hasPort = runCatching { URI("http://$i").port != -1 }.getOrDefault(false)
            candidates += listOf("https://$i", "http://$i")
            if (!hasPort) candidates += listOf("http://$i:8096", "https://$i:8920")
        }
        // All candidates are tried at once (worst case is one timeout, not one per address), but
        // the answer is taken in preference order, so https still wins over http when both work.
        val attempts = candidates.map { c -> async { probeOne(c) } }
        var last: ApiException? = null
        try {
            for (a in attempts) {
                val (url, info, error) = a.await()
                if (info != null) return@coroutineScope url to info
                last = error ?: last
            }
        } finally {
            attempts.forEach { it.cancel() }
        }
        throw last ?: ApiException("Couldn't find a Jellyfin server at that address.")
    }

    private suspend fun probeOne(c: String): Triple<String, PublicSystemInfo?, ApiException?> = try {
        val info = withTimeout(6000) {
            send(Request.Builder().url("$c/System/Info/Public").header("Accept", "application/json").build(), PublicSystemInfo.serializer())
        }
        if (!info.id.isNullOrEmpty()) Triple(c, info, null) else Triple(c, null, ApiException("$c doesn't look like a Jellyfin server."))
    } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
        Triple(c, null, ApiException("Timed out connecting to $c"))
    } catch (e: CancellationException) {
        throw e
    } catch (e: ApiException) {
        Triple(c, null, if (e.cause is SerializationException) ApiException("$c doesn't look like a Jellyfin server.") else e)
    } catch (e: Exception) {
        Triple(c, null, ApiException("Can't reach $c. ${e.message ?: ""}".trim(), null, e))
    }

    suspend fun getPublicUsers(): List<UserDto> = send(req("GET", "/Users/Public"), ListSerializer(UserDto.serializer()))

    suspend fun authenticate(user: String, password: String): AuthenticationResult {
        val res = send(req("POST", "/Users/AuthenticateByName", json(AuthenticateUserByName(user, password), AuthenticateUserByName.serializer())),
            AuthenticationResult.serializer())
        token = res.accessToken
        userId = res.user?.id
        return res
    }

    suspend fun isQuickConnectEnabled(): Boolean = try {
        sendRaw(req("GET", "/QuickConnect/Enabled")).trim() == "true"
    } catch (e: CancellationException) { throw e } catch (_: Exception) { false }

    suspend fun quickConnectInitiate(): QuickConnectResult = send(req("POST", "/QuickConnect/Initiate"), QuickConnectResult.serializer())

    suspend fun quickConnectState(secret: String): QuickConnectResult =
        send(req("GET", "/QuickConnect/Connect" + Query().add("secret", secret)), QuickConnectResult.serializer())

    suspend fun quickConnectAuthenticate(secret: String): AuthenticationResult {
        val res = send(req("POST", "/Users/AuthenticateWithQuickConnect", json(QuickConnectDto(secret), QuickConnectDto.serializer())),
            AuthenticationResult.serializer())
        token = res.accessToken
        userId = res.user?.id
        return res
    }

    suspend fun getMe(): UserDto = send(req("GET", "/Users/Me"), UserDto.serializer())

    suspend fun logout() {
        try { sendRaw(req("POST", "/Sessions/Logout")) } catch (e: CancellationException) { throw e } catch (_: Exception) { /* best effort */ }
    }

    // ---------- Browsing ----------

    private val itemsResult = QueryResult.serializer(BaseItemDto.serializer())
    private val itemList = ListSerializer(BaseItemDto.serializer())

    suspend fun getViews(): QueryResult<BaseItemDto> = send(req("GET", "/UserViews" + Query().add("userId", userId)), itemsResult)

    suspend fun getResume(limit: Int = 16): QueryResult<BaseItemDto> = send(req("GET", "/UserItems/Resume" + Query()
        .add("userId", userId).add("limit", limit).add("mediaTypes", "Video")
        .add("fields", CARD_FIELDS).add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").add("imageTypeLimit", 1)), itemsResult)

    suspend fun getNextUp(limit: Int = 16, seriesId: String? = null): QueryResult<BaseItemDto> = send(req("GET", "/Shows/NextUp" + Query()
        .add("userId", userId).add("limit", limit).add("seriesId", seriesId).add("fields", CARD_FIELDS)
        .add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").add("imageTypeLimit", 1).add("enableResumable", false)), itemsResult)

    suspend fun getLatest(parentId: String?, limit: Int = 20, includeTypes: String? = null): List<BaseItemDto> = send(req("GET", "/Items/Latest" + Query()
        .add("userId", userId).add("parentId", parentId).add("limit", limit).add("includeItemTypes", includeTypes)
        .add("fields", LIST_FIELDS).add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").add("imageTypeLimit", 1)), itemList)

    suspend fun getItems(q: ItemQuery): QueryResult<BaseItemDto> = send(req("GET", "/Items" + Query()
        .add("userId", userId).add("parentId", q.parentId).add("includeItemTypes", q.includeItemTypes)
        .add("excludeItemTypes", q.excludeItemTypes)
        .add("recursive", q.recursive).add("sortBy", q.sortBy).add("sortOrder", q.sortOrder)
        .add("startIndex", q.startIndex).add("limit", q.limit).add("filters", q.filters)
        .add("searchTerm", q.searchTerm).add("genres", q.genres).add("personIds", q.personIds)
        .add("ids", q.ids).add("years", q.years).add("is4K", q.is4K).add("isHd", q.isHd).add("nameLessThan", q.nameLessThan)
        .add("isMissing", q.isMissing)
        .add("fields", q.fields ?: CARD_FIELDS).add("imageTypeLimit", 1)
        .add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").add("enableTotalRecordCount", true)), itemsResult)

    suspend fun getItem(id: String): BaseItemDto =
        send(req("GET", "/Items/$id" + Query().add("userId", userId).add("fields", DETAIL_FIELDS)), BaseItemDto.serializer())

    suspend fun getSeasons(seriesId: String): QueryResult<BaseItemDto> =
        send(req("GET", "/Shows/$seriesId/Seasons" + Query().add("userId", userId).add("fields", CARD_FIELDS)), itemsResult)

    suspend fun getEpisodes(seriesId: String, seasonId: String?): QueryResult<BaseItemDto> = send(req("GET", "/Shows/$seriesId/Episodes" + Query()
        .add("userId", userId).add("seasonId", seasonId).add("fields", EPISODE_FIELDS)), itemsResult)

    /** Just the episodes either side of [episodeId] (plus itself), in series order. */
    suspend fun getAdjacentEpisodes(seriesId: String, episodeId: String): QueryResult<BaseItemDto> = send(req("GET", "/Shows/$seriesId/Episodes" + Query()
        .add("userId", userId).add("adjacentTo", episodeId).add("fields", "PrimaryImageAspectRatio")
        .add("enableImageTypes", "Primary,Thumb").add("imageTypeLimit", 1)), itemsResult)

    /** One episode of a series by season and episode number (no full series listing). */
    suspend fun findEpisode(seriesId: String, season: Int, episode: Int): BaseItemDto? {
        val r = send(req("GET", "/Items" + Query()
            .add("userId", userId).add("parentId", seriesId).add("includeItemTypes", "Episode").add("recursive", true)
            .add("parentIndexNumber", season).add("indexNumber", episode).add("limit", 5)), itemsResult)
        // Older servers ignore the number filters, so check the numbers ourselves.
        return r.items.firstOrNull { it.parentIndexNumber == season && it.indexNumber == episode }
    }

    /** The item's trickplay (scrubbing thumbnail) layouts, or null when it has none. */
    suspend fun getTrickplay(itemId: String): Map<String, Map<String, TrickplayInfo>>? = try {
        send(req("GET", "/Items/$itemId" + Query().add("userId", userId).add("fields", "Trickplay")), BaseItemDto.serializer()).trickplay
    } catch (e: ApiException) { null }

    /** One sheet of trickplay thumbnails. No token in the URL: the image client sends it as a header. */
    fun trickplayTileUrl(itemId: String, mediaSourceId: String, width: Int, index: Int): String =
        "$baseUrl/Videos/$itemId/Trickplay/$width/$index.jpg" + Query().add("mediaSourceId", mediaSourceId)

    /** Trailers stored next to the item (local files only; online trailers are never used). */
    suspend fun getLocalTrailers(id: String): List<BaseItemDto> = try {
        send(req("GET", "/Items/$id/LocalTrailers" + Query().add("userId", userId)), itemList)
    } catch (e: ApiException) { emptyList() }

    /** Extras (behind the scenes, deleted scenes, featurettes…) stored with the item. */
    suspend fun getSpecialFeatures(id: String): List<BaseItemDto> = try {
        send(req("GET", "/Items/$id/SpecialFeatures" + Query().add("userId", userId)), itemList)
    } catch (e: ApiException) { emptyList() }

    /** Genres used in a library (names only). */
    suspend fun getGenres(parentId: String?, includeTypes: String?): List<String> = send(req("GET", "/Genres" + Query()
        .add("userId", userId).add("parentId", parentId).add("includeItemTypes", includeTypes).add("sortBy", "SortName")
        .add("enableImages", false).add("enableTotalRecordCount", false)), itemsResult).items.mapNotNull { it.name?.takeIf { n -> n.isNotEmpty() } }

    // ---------- Subtitles found by the server (its subtitle provider plugins do the searching) ----------

    suspend fun searchRemoteSubtitles(itemId: String, language: String): List<RemoteSubtitleInfo> =
        send(req("GET", "/Items/$itemId/RemoteSearch/Subtitles/${Query.escape(language)}"), ListSerializer(RemoteSubtitleInfo.serializer()))

    suspend fun downloadRemoteSubtitle(itemId: String, subtitleId: String) {
        sendRaw(req("POST", "/Items/$itemId/RemoteSearch/Subtitles/${Query.escape(subtitleId)}"))
    }

    // ---------- Session capabilities (remote control from other Jellyfin apps) ----------

    suspend fun postCapabilities(caps: ClientCapabilitiesDto) {
        sendRaw(req("POST", "/Sessions/Capabilities/Full", json(caps, ClientCapabilitiesDto.serializer())))
    }

    /**
     * The server's WebSocket address, or null when not configured. Given as http(s)://, which
     * OkHttp's WebSocket client upgrades itself (ws/wss on the wire).
     */
    fun socketUrl(): String? = baseUrl?.let { "$it/socket" + Query().add("deviceId", deviceId) }

    suspend fun getSimilar(id: String, limit: Int = 16): QueryResult<BaseItemDto> =
        send(req("GET", "/Items/$id/Similar" + Query().add("userId", userId).add("limit", limit).add("fields", CARD_FIELDS)), itemsResult)

    suspend fun setPlayed(id: String, played: Boolean) {
        sendRaw(req(if (played) "POST" else "DELETE", "/UserPlayedItems/$id" + Query().add("userId", userId)))
    }

    suspend fun setFavorite(id: String, fav: Boolean) {
        sendRaw(req(if (fav) "POST" else "DELETE", "/UserFavoriteItems/$id" + Query().add("userId", userId)))
    }

    suspend fun getPlugins(): List<PluginInfo> = send(req("GET", "/Plugins"), ListSerializer(PluginInfo.serializer()))

    // ---------- Playback (direct play only) ----------

    suspend fun getPlaybackInfo(itemId: String, startTicks: Long?): PlaybackInfoResponse =
        send(req("POST", "/Items/$itemId/PlaybackInfo" + Query().add("userId", userId),
            json(PlaybackInfoDto(userId = userId, startTimeTicks = startTicks), PlaybackInfoDto.serializer())), PlaybackInfoResponse.serializer())

    /**
     * The original file, byte-for-byte (static=true). This is the only video URL Nettle ever builds.
     * It carries no token: the player sends [tokenHeaderValue] as a header instead.
     */
    fun directStreamUrl(itemId: String, mediaSourceId: String?, playSessionId: String?): String =
        "$baseUrl/Videos/$itemId/stream" + Query()
            .add("static", true).add("mediaSourceId", mediaSourceId).add("playSessionId", playSessionId)
            .add("deviceId", deviceId)

    /** An external subtitle file as stored on the server (no conversion). Token goes in a header. */
    fun externalSubtitleUrl(itemId: String, mediaSourceId: String, s: MediaStream): String {
        val codec = when (val c = s.codec?.lowercase()) { null, "" -> "srt"; "subrip" -> "srt"; "webvtt" -> "vtt"; else -> c }
        return "$baseUrl/Videos/$itemId/$mediaSourceId/Subtitles/${s.index}/0/Stream.$codec"
    }

    suspend fun reportStart(p: PlaybackProgressInfo) = postReport("/Sessions/Playing", p)
    suspend fun reportProgress(p: PlaybackProgressInfo) = postReport("/Sessions/Playing/Progress", p)
    suspend fun reportStopped(p: PlaybackProgressInfo) = postReport("/Sessions/Playing/Stopped", p)

    private suspend fun postReport(path: String, p: PlaybackProgressInfo) {
        try { sendRaw(req("POST", path, json(p, PlaybackProgressInfo.serializer()))) }
        catch (e: CancellationException) { throw e } catch (_: Exception) { /* reporting must never break playback */ }
    }

    // ---------- Plugin-provided segments ----------

    /** Jellyfin 10.10+ Media Segments API (populated by Intro Skipper and other segment plugins). */
    suspend fun getMediaSegments(itemId: String): List<MediaSegmentDto> = try {
        send(req("GET", "/MediaSegments/$itemId"), QueryResult.serializer(MediaSegmentDto.serializer())).items
    } catch (e: ApiException) { emptyList() }

    /** Intro Skipper plugin's own endpoint (works on servers before 10.10 too). */
    suspend fun getIntroSkipperSegments(itemId: String): Map<String, IntroSkipperSegment> = try {
        send(req("GET", "/Episode/$itemId/IntroSkipperSegments"), MapSerializer(String.serializer(), IntroSkipperSegment.serializer()))
    } catch (e: ApiException) { emptyMap() }

    // ---------- Images ----------

    fun imageUrl(itemId: String?, type: String, tag: String?, maxWidth: Int, quality: Int = 90): String? {
        if (itemId == null || baseUrl == null) return null
        // WebP: about a third smaller than JPEG at the same quality, and keeps logos' transparency.
        return "$baseUrl/Items/$itemId/Images/$type" + Query().add("tag", tag).add("maxWidth", maxWidth).add("quality", quality).add("format", "Webp")
    }

    fun userImageUrl(userId: String?, tag: String?, maxWidth: Int = 200, baseUrl: String? = null): String? {
        if (userId == null || tag == null) return null
        return "${(baseUrl ?: this.baseUrl)?.trimEnd('/')}/Users/$userId/Images/Primary" + Query().add("tag", tag).add("maxWidth", maxWidth)
    }
}

data class ItemQuery(
    var parentId: String? = null,
    var includeItemTypes: String? = null,
    var excludeItemTypes: String? = null,
    var recursive: Boolean? = true,
    var sortBy: String? = "SortName",
    var sortOrder: String? = "Ascending",
    var startIndex: Int? = null,
    var limit: Int? = null,
    var filters: String? = null,
    var searchTerm: String? = null,
    var genres: String? = null,
    var personIds: String? = null,
    var fields: String? = null,
    /** Comma-separated item ids (e.g. what a remote control asked to play). */
    var ids: String? = null,
    /** Comma-separated years. */
    var years: String? = null,
    var is4K: Boolean? = null,
    var isHd: Boolean? = null,
    /** Only items whose sort name comes before this (used to count where a letter starts). */
    var nameLessThan: String? = null,
    /** false = leave out placeholder episodes that have no file. */
    var isMissing: Boolean? = null,
)
