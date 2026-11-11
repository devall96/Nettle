package io.github.devall96.nettle.core.services

/**
 * Turns an error that closed the app into a short text a person can read on a TV and photograph:
 * what the app and device are, then each error in the chain with the first few places it passed through.
 * Tokens and keys are masked the same way as in the log.
 */
object CrashReport {
    /** [header]: app version, Android version and device, e.g. "Nettle 1.0.1 · Android 9 · Hisense TV". */
    fun format(error: Throwable, header: String, time: String, maxFrames: Int = 8, maxCauses: Int = 3): String {
        val sb = StringBuilder()
        sb.append(header).append('\n').append(time).append("\n\n")
        var e: Throwable? = error
        var n = 0
        val seen = HashSet<Throwable>()
        while (e != null && n < maxCauses && seen.add(e)) {
            if (n > 0) sb.append("Caused by: ")
            sb.append(e.javaClass.name)
            val msg = Log.redact(e.message).replace('\n', ' ').trim()
            if (msg.isNotEmpty()) sb.append(": ").append(if (msg.length > 300) msg.substring(0, 300) + "…" else msg)
            sb.append('\n')
            // The coroutine machinery's own steps say nothing about what went wrong: leave them out.
            val frames = e.stackTrace.filterNot { it.className.startsWith("kotlinx.coroutines.") || it.className.startsWith("kotlin.coroutines.") }
            for (i in 0 until minOf(maxFrames, frames.size)) sb.append("  at ").append(shorten(frames[i])).append('\n')
            if (frames.size > maxFrames) sb.append("  … ").append(frames.size - maxFrames).append(" more\n")
            e = e.cause
            n++
        }
        return sb.toString().trimEnd()
    }

    /** "io.github.devall96.nettle.core.api.Query.add(Http.kt:49)" → "core.api.Query.add (Http.kt:49)". */
    private fun shorten(f: StackTraceElement): String {
        val cls = f.className.removePrefix("io.github.devall96.nettle.")
        val where = if (f.fileName != null && f.lineNumber >= 0) " (${f.fileName}:${f.lineNumber})" else ""
        return "$cls.${f.methodName}$where"
    }
}
