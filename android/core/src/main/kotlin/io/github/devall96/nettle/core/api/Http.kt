package io.github.devall96.nettle.core.api

import kotlinx.coroutines.suspendCancellableCoroutine
import okhttp3.Call
import okhttp3.Callback
import okhttp3.OkHttpClient
import okhttp3.Response
import java.io.IOException
import java.net.URLEncoder
import java.util.concurrent.TimeUnit
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/** Port of Nettle.Core.Api.ApiException. */
class ApiException(message: String, val statusCode: Int? = null, cause: Throwable? = null) : Exception(message, cause) {
    val isUnauthorized: Boolean get() = statusCode == 401 || statusCode == 403
}

object HttpFactory {
    /**
     * Base settings for every client in the app. The app adds its host-allowlist interceptor on top
     * (one shared connection pool), so nothing can reach a host the user didn't configure.
     */
    fun builder(base: OkHttpClient? = null, timeoutSeconds: Long = 30): OkHttpClient.Builder =
        (base?.newBuilder() ?: OkHttpClient.Builder())
            .connectTimeout(8, TimeUnit.SECONDS)
            .callTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .readTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .followRedirects(true)
}

/** Suspends until the call completes; cancelling the coroutine cancels the request. */
suspend fun Call.await(): Response = suspendCancellableCoroutine { cont ->
    cont.invokeOnCancellation { runCatching { cancel() } }
    enqueue(object : Callback {
        override fun onFailure(call: Call, e: IOException) { if (cont.isActive) cont.resumeWithException(e) }
        override fun onResponse(call: Call, response: Response) {
            if (cont.isActive) cont.resume(response) else response.close()
        }
    })
}

/** Tiny query-string builder that skips nulls (port of Nettle.Core.Api.Query). */
class Query {
    private val sb = StringBuilder()

    fun add(key: String, value: String?): Query {
        if (value.isNullOrEmpty()) return this
        sb.append(if (sb.isEmpty()) '?' else '&').append(key).append('=').append(escape(value))
        return this
    }
    fun add(key: String, value: Int?): Query = if (value != null) add(key, value.toString()) else this
    fun add(key: String, value: Long?): Query = if (value != null) add(key, value.toString()) else this
    fun add(key: String, value: Boolean?): Query = if (value != null) add(key, if (value) "true" else "false") else this

    override fun toString(): String = sb.toString()

    companion object {
        /** Like .NET's Uri.EscapeDataString (RFC 3986: spaces as %20, not '+'). */
        fun escape(s: String): String = URLEncoder.encode(s, Charsets.UTF_8).replace("+", "%20").replace("%7E", "~").replace("*", "%2A")
    }
}
