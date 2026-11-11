package io.github.devall96.nettle.tv.platform

import android.content.Context
import android.os.Build
import io.github.devall96.nettle.core.services.CrashReport
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Remembers why Nettle closed unexpectedly. An error nothing handled is written to a small file in the app's
 * private folder before Android closes the app; the next start shows it (see CrashModel) instead of going
 * straight back to the screen that failed. Nothing is sent anywhere: the text only ever appears on this TV.
 */
object CrashLog {
    private var file: File? = null

    fun install(context: Context, version: String) {
        val f = File(context.filesDir, "crash.txt")
        file = f
        val header = "Nettle $version · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT}) · ${Build.MANUFACTURER} ${Build.MODEL}"
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, error ->
            // Written at once, on this thread: the process is about to end.
            runCatching {
                val time = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.ROOT).format(Date())
                f.writeText(CrashReport.format(error, header, time))
            }
            if (previous != null) previous.uncaughtException(thread, error)
            else { android.os.Process.killProcess(android.os.Process.myPid()); System.exit(10) }
        }
    }

    /** What closed the app last time, if anything did and it hasn't been dismissed yet. */
    fun pending(): String? = runCatching { file?.takeIf { it.exists() }?.readText()?.takeIf { it.isNotBlank() } }.getOrNull()

    fun clear() { runCatching { file?.delete() } }
}
