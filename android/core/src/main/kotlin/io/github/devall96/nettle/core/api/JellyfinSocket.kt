package io.github.devall96.nettle.core.api

import io.github.devall96.nettle.core.services.Log
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.serialization.json.*
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.concurrent.TimeUnit

/**
 * The Jellyfin server's WebSocket (/socket), a port of JellyfinSocket.cs: how the server reaches
 * this app (remote control from other Jellyfin apps, watched-state changes made elsewhere, new
 * library items). Only ever connects to the signed-in server; the token travels in a header.
 * Reconnects by itself (with back-off) until [stop].
 */
class JellyfinSocket(private val client: JellyfinClient, base: OkHttpClient) {
    private val http = base.newBuilder().readTimeout(0, TimeUnit.MILLISECONDS).callTimeout(0, TimeUnit.MILLISECONDS)
        .pingInterval(30, TimeUnit.SECONDS).build()
    private var job: Job? = null

    /** Called on a background thread with the message type and its "Data" (may be null). */
    var onMessage: ((String, JsonElement?) -> Unit)? = null
    /** Called (background thread) each time a connection is established. */
    var onConnected: (() -> Unit)? = null

    @Volatile var isConnected = false; private set

    fun start(scope: CoroutineScope) {
        stop()
        job = scope.launch(Dispatchers.IO) { run() }
    }

    fun stop() {
        job?.cancel()
        job = null
        isConnected = false
    }

    private sealed interface Ev
    private data class Text(val s: String) : Ev
    private data class Closed(val error: Throwable?) : Ev
    private data object Opened : Ev

    private suspend fun run() {
        var failures = 0
        try {
            while (currentCoroutineContext().isActive) {
                val url = client.socketUrl()
                if (url == null || client.tokenHeaderValue == null) return
                val events = Channel<Ev>(Channel.UNLIMITED)
                val ws = http.newWebSocket(Request.Builder().url(url).header("Authorization", client.authorizationHeaderValue).build(),
                    object : WebSocketListener() {
                        override fun onOpen(webSocket: WebSocket, response: Response) { events.trySend(Opened) }
                        override fun onMessage(webSocket: WebSocket, text: String) { events.trySend(Text(text)) }
                        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) { webSocket.close(1000, null); events.trySend(Closed(null)) }
                        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { events.trySend(Closed(null)) }
                        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { events.trySend(Closed(t)) }
                    })
                var keepAlive: Job? = null
                try {
                    coroutineScope {
                        val first = withTimeoutOrNull(15_000) { events.receive() }
                        if (first !is Opened) throw (first as? Closed)?.error ?: java.io.IOException("connect timed out")
                        failures = 0
                        isConnected = true
                        Log.info("server connection (WebSocket) open")
                        runCatching { onConnected?.invoke() }.onFailure { Log.warn("socket connected handler: ${it.message}") }
                        while (true) {
                            when (val e = events.receive()) {
                                is Closed -> { if (e.error != null) throw e.error; return@coroutineScope }
                                is Text -> {
                                    val root = runCatching { Json.parseToJsonElement(e.s).jsonObject }.getOrNull() ?: continue
                                    val type = (root["MessageType"] as? JsonPrimitive)?.contentOrNull ?: ""
                                    val data = root["Data"]
                                    if (type == "ForceKeepAlive") {
                                        // The server drops clients that go quiet: answer at half its timeout.
                                        val seconds = (data as? JsonPrimitive)?.intOrNull ?: 60
                                        val period = maxOf(5_000L, seconds * 500L)
                                        keepAlive?.cancel()
                                        keepAlive = launch { while (isActive) { ws.send("{\"MessageType\":\"KeepAlive\"}"); delay(period) } }
                                        continue
                                    }
                                    if (type == "KeepAlive") continue
                                    runCatching { onMessage?.invoke(type, data) }.onFailure { Log.warn("socket message $type failed: ${it.message}") }
                                }
                                Opened -> {}
                            }
                        }
                    }
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    failures++
                    if (failures <= 3) Log.info("server connection (WebSocket) dropped: ${e.message}")
                } finally {
                    keepAlive?.cancel()
                    isConnected = false
                    ws.cancel()
                }
                // 2 s, 5 s, 15 s, then every 30 s while the server is away.
                delay(when (failures) { 0, 1 -> 2_000L; 2 -> 5_000L; 3 -> 15_000L; else -> 30_000L })
            }
        } finally {
            Log.info("server connection (WebSocket) closed")
        }
    }
}
