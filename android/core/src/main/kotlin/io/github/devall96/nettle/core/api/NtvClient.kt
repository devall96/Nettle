package io.github.devall96.nettle.core.api

import io.github.devall96.nettle.core.models.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import org.xmlpull.v1.XmlPullParser
import org.xmlpull.v1.XmlPullParserFactory
import java.io.IOException
import java.io.InputStream
import java.net.URI
import java.time.LocalDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

/**
 * NostalgiaTV integration (port of Nettle.Core.Api.NtvClient) using the server's standard tuner outputs:
 *   GET /channels.m3u  (channel list + stream URLs)
 *   GET /xmltv.xml     (program guide)
 * Streams are MPEG-TS played as they are. Only live channels are ever exposed; the
 * NostalgiaTV on-demand section is deliberately not integrated.
 */
class NtvClient(base: OkHttpClient) {
    private val http = HttpFactory.builder(base, 60).build()

    var baseUrl: String? = null; private set
    var apiKey: String? = null; private set
    val isConfigured: Boolean get() = baseUrl != null

    fun configure(baseUrl: String?, apiKey: String?) {
        if (baseUrl.isNullOrBlank()) { this.baseUrl = null; return }
        var u = baseUrl.trim().trimEnd('/')
        if (!u.startsWith("http", ignoreCase = true)) u = "http://$u"
        val parsed = runCatching { URI(u) }.getOrNull()
        if (parsed != null && parsed.port == -1 && parsed.scheme == "http" && u.indexOf(':', 7) < 0) u += ":19850"
        this.baseUrl = u
        this.apiKey = apiKey?.trim()?.takeIf { it.isNotEmpty() }
        guide = null // different server or key: forget the cached guide
    }

    /** True if a NostalgiaTV server answers at [baseUrl] (its channel list is reachable). */
    suspend fun probe(baseUrl: String, timeoutMs: Long): Boolean = withTimeoutOrNull(timeoutMs) { withContext(Dispatchers.IO) {
        // All on a background thread: closing a half-read response touches the network (not allowed on Android's main thread).
        try {
            http.newCall(Request.Builder().url(baseUrl.trimEnd('/') + "/channels.m3u").build()).await().use { r ->
                if (!r.isSuccessful) return@use false
                val head = withContext(Dispatchers.IO) { r.body.source().let { s -> s.request(16); s.buffer.readUtf8(minOf(16, s.buffer.size)) } }
                val ok = head.trimStart('﻿').startsWith("#EXTM3U")
                if (!ok) io.github.devall96.nettle.core.services.Log.info("NostalgiaTV probe at $baseUrl: not a channel list (${head.take(10)})")
                ok
            }
        } catch (e: CancellationException) { throw e } catch (e: Exception) { io.github.devall96.nettle.core.services.Log.info("NostalgiaTV probe at $baseUrl failed: $e"); false }
    } } ?: false.also { io.github.devall96.nettle.core.services.Log.info("NostalgiaTV probe at $baseUrl timed out") }

    // The parsed guide is kept for a while: re-downloading and parsing several MB of XMLTV on
    // every visit to the Live TV page was the slowest part of opening it.
    private val guideMaxAgeMs = 10 * 60_000L
    private val guideGate = Mutex()
    private var guide: List<NtvChannel>? = null
    private var guideAt = 0L
    private var guideDays = 0

    /** When the cached guide was downloaded (epoch ms, null = nothing cached). */
    val guideLoadedAt: Long? get() = if (guide == null) null else guideAt

    /** Channels plus the guide for [now - 3h, now + days]. Served from memory when fresh enough, unless [force]. */
    suspend fun loadGuide(days: Int = 3, force: Boolean = false): List<NtvChannel> = guideGate.withLock {
        val g0 = guide
        if (!force && g0 != null && guideDays >= days && System.currentTimeMillis() - guideAt < guideMaxAgeMs) return@withLock g0
        val g = loadGuideCore(days)
        guide = g
        guideAt = System.currentTimeMillis()
        guideDays = days
        g
    }

    private suspend fun get(path: String): Response {
        val b = Request.Builder().url(baseUrl + path)
        apiKey?.let { b.header("X-API-Key", it) }
        val resp = try { http.newCall(b.build()).await() } catch (e: CancellationException) { throw e } catch (e: IOException) {
            throw ApiException("Can't reach NostalgiaTV. ${e.message ?: ""}".trim(), null, e)
        }
        if (!resp.isSuccessful) {
            resp.close()
            throw ApiException("NostalgiaTV returned ${resp.code} for $path. Is the HDHR/IPTV tuner enabled?", resp.code)
        }
        return resp
    }

    private suspend fun loadGuideCore(days: Int): List<NtvChannel> = withContext(Dispatchers.IO) { coroutineScope {
        if (baseUrl == null) throw ApiException("NostalgiaTV isn't set up yet. Add its address in Settings.")
        val xml = async { get("/xmltv.xml") }
        val channels = try {
            get("/channels.m3u").use { r -> withContext(Dispatchers.IO) { parseM3u(r.body.string()) } }
        } catch (e: Throwable) {
            // The guide download is already under way: close it rather than leave it open.
            runCatching { xml.cancel(); if (xml.isCompleted) xml.getCompleted().close() }
            throw e
        }
        val now = System.currentTimeMillis()
        xml.await().use { r ->
            withContext(Dispatchers.Default) { parseXmltv(r.body.byteStream(), channels, now - 3 * 3600_000L, now + days * 86_400_000L) }
        }
        channels.forEach { c -> c.programs.sortBy { it.start } }
        channels
    } }

    /**
     * The schedule rows for one channel (now → now + hours). Each row names the media-server
     * item that airs in that slot, which is what enables original-quality direct play.
     */
    suspend fun getChannelSchedule(stableId: String, hours: Int = 6): List<NtvSlot> {
        if (baseUrl == null) return emptyList()
        val text = get("/api/schedule?hours=$hours&channelId=${Query.escape(stableId)}").use { withContext(Dispatchers.IO) { it.body.string() } }
        val resp = runCatching { NettleJson.camel.decodeFromString(NtvScheduleResponse.serializer(), text) }.getOrNull()
        val ch = resp?.channels?.firstOrNull { it.stableId.equals(stableId, ignoreCase = true) } ?: resp?.channels?.firstOrNull()
        return (ch?.programs ?: emptyList()).sortedBy { it.startTimeMs }
    }

    private val attr = Regex("([A-Za-z][\\w-]*)=\"([^\"]*)\"")

    internal fun parseM3u(text: String): MutableList<NtvChannel> {
        val list = ArrayList<NtvChannel>()
        var attrs: Map<String, String>? = null
        var title: String? = null
        for (raw in text.lineSequence()) {
            val line = raw.trim()
            if (line.isEmpty()) continue
            if (line.startsWith("#EXTINF", ignoreCase = true)) {
                attrs = attr.findAll(line).associate { it.groupValues[1].lowercase() to it.groupValues[2] }
                val comma = line.lastIndexOf(',')
                title = if (comma >= 0) line.substring(comma + 1).trim() else null
            } else if (!line.startsWith('#') && attrs != null) {
                val url = if (line.startsWith("http", ignoreCase = true)) line else baseUrl + "/" + line.trimStart('/')
                // Security: only ever hand http(s) URLs to the player (never file://, smb://, etc.).
                val scheme = runCatching { URI(url).scheme }.getOrNull()
                if (scheme != "http" && scheme != "https") { attrs = null; title = null; continue }
                val id = attrs["tvg-id"]
                val chno = attrs["tvg-chno"]
                val logo = attrs["tvg-logo"]
                list += NtvChannel(
                    id = if (id.isNullOrEmpty()) (chno ?: title ?: url) else id,
                    name = title ?: attrs["tvg-name"] ?: id ?: "Channel",
                    number = chno ?: attrs["channel-number"],
                    logoUrl = logo?.takeIf { it.isNotEmpty() },
                    streamUrl = url,
                    group = attrs["group-title"],
                )
                attrs = null
                title = null
            }
        }
        return list
    }

    companion object {
        internal fun parseXmltv(s: InputStream, channels: List<NtvChannel>, from: Long, to: Long) {
            val byId = HashMap<String, NtvChannel>()
            channels.forEach { byId.putIfAbsent(it.id.lowercase(), it) }
            val f = XmlPullParserFactory.newInstance()
            f.isNamespaceAware = false
            val p = f.newPullParser()
            p.setInput(s, null)
            // DOCDECL / entities are never resolved: XmlPullParser doesn't fetch external DTDs.
            var ev = p.eventType
            while (ev != XmlPullParser.END_DOCUMENT) {
                if (ev == XmlPullParser.START_TAG) {
                    when (p.name) {
                        "channel" -> {
                            val id = p.getAttributeValue(null, "id")
                            var icon: String? = null
                            val depth = p.depth
                            while (true) {
                                val e = p.next()
                                if (e == XmlPullParser.END_DOCUMENT || (e == XmlPullParser.END_TAG && p.depth == depth)) break
                                if (e == XmlPullParser.START_TAG && p.name == "icon") icon = p.getAttributeValue(null, "src")
                            }
                            val ch = id?.let { byId[it.lowercase()] }
                            if (ch != null && ch.logoUrl == null) ch.logoUrl = icon
                        }
                        "programme" -> readProgramme(p, byId, from, to)
                    }
                }
                ev = p.next()
            }
        }

        private fun readProgramme(p: XmlPullParser, byId: Map<String, NtvChannel>, from: Long, to: Long) {
            val chId = p.getAttributeValue(null, "channel")
            val start = parseXmltvTime(p.getAttributeValue(null, "start"))
            val stop = parseXmltvTime(p.getAttributeValue(null, "stop"))
            val ch = chId?.let { byId[it.lowercase()] }
            val depth = p.depth
            val keep = ch != null && start != null && stop != null && stop >= from && start <= to
            var title = ""; var sub = ""; var desc = ""; var ep = ""; var icon = ""; var cat = ""
            while (true) {
                val e = p.next()
                if (e == XmlPullParser.END_DOCUMENT || (e == XmlPullParser.END_TAG && p.depth == depth)) break
                if (!keep || e != XmlPullParser.START_TAG) continue
                when (p.name) {
                    "title" -> { val t = p.nextText(); if (title.isEmpty()) title = t }
                    "sub-title" -> sub = p.nextText()
                    "desc" -> desc = p.nextText()
                    "episode-num" -> {
                        val sys = p.getAttributeValue(null, "system")
                        var v = p.nextText()
                        if (sys == "xmltv_ns") v = fromXmltvNs(v) ?: ""
                        if (v.isNotEmpty() && (ep.isEmpty() || sys == "onscreen")) ep = v
                    }
                    "icon" -> icon = p.getAttributeValue(null, "src") ?: ""
                    "category" -> { val c = p.nextText(); if (cat.isEmpty()) cat = c }
                }
            }
            if (!keep) return
            ch!!.programs += NtvProgram(start!!, stop!!, title, sub.ifEmpty { null }, desc.ifEmpty { null }, ep.ifEmpty { null },
                icon.ifEmpty { null }, cat.ifEmpty { null })
        }

        /** xmltv_ns is zero-based "season.episode.part" (e.g. "0.2.0/1") → "S1:E3". */
        fun fromXmltvNs(v: String): String? {
            val parts = v.split('.')
            if (parts.size < 2) return null
            fun num(s: String): Int? = s.split('/')[0].trim().toIntOrNull()?.plus(1)
            val season = num(parts[0])
            val episode = num(parts[1])
            if (season != null && episode != null) return "S$season:E$episode"
            if (episode != null) return "E$episode"
            return null
        }

        private val xmltvFmt = DateTimeFormatter.ofPattern("yyyyMMddHHmmss")

        /** XMLTV time "yyyyMMddHHmmss +hhmm" (offset optional) → epoch ms. */
        fun parseXmltvTime(v: String?): Long? {
            if (v == null || v.length < 12) return null
            val digits = if (v.length >= 14) v.substring(0, 14) else v.substring(0, 12) + "00"
            val dt = runCatching { LocalDateTime.parse(digits, xmltvFmt) }.getOrNull() ?: return null
            var offset = ZoneOffset.UTC
            val rest = if (v.length > 14) v.substring(14).trim() else ""
            if (rest.length >= 5 && (rest[0] == '+' || rest[0] == '-')) {
                val hh = rest.substring(1, 3).toIntOrNull()
                val mm = rest.substring(3, 5).toIntOrNull()
                if (hh != null && mm != null) {
                    val secs = (hh * 3600 + mm * 60) * if (rest[0] == '-') -1 else 1
                    offset = ZoneOffset.ofTotalSeconds(secs)
                }
            }
            return dt.toInstant(offset).toEpochMilli()
        }
    }
}
