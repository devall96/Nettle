package io.github.devall96.nettle.tv.platform

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import io.github.devall96.nettle.core.api.await
import io.github.devall96.nettle.core.models.NettleJson
import io.github.devall96.nettle.core.services.AvailableUpdate
import io.github.devall96.nettle.core.services.GitHubRelease
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.core.services.UpdateCheck
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Request
import java.io.File
import java.io.IOException

/**
 * Android TV's updater, the same idea as the Windows / Linux one (Settings → Updates): it only ever
 * contacts the update source (Nettle's GitHub Releases by default), only when asked or when "check
 * automatically" is on, and installs nothing without the TV's own install prompt. The newest
 * release's Nettle-AndroidTV.apk is downloaded and handed to Android's package installer, which
 * keeps settings and sign-ins (same app, same signing key).
 */
object Updater {
    /** GitHub's API and the hosts its release downloads redirect to: reachable only while updating. */
    private val gitHubHosts = listOf(
        "https://api.github.com", "https://github.com",
        "https://objects.githubusercontent.com", "https://release-assets.githubusercontent.com",
    )

    private fun allowGitHub() = gitHubHosts.forEach { AppServices.allowHostOf(it) }

    /** The newer release on offer, or null when Nettle is up to date (throws when the check fails). */
    suspend fun check(source: String): AvailableUpdate? {
        val api = UpdateCheck.latestReleaseApi(source)
            ?: throw IOException("On Android TV, updates come from a GitHub repository (Nettle's own is ${io.github.devall96.nettle.core.models.AppSettings.OFFICIAL_UPDATE_SOURCE}).")
        allowGitHub()
        val request = Request.Builder().url(api)
            .header("Accept", "application/vnd.github+json")
            .header("User-Agent", "Nettle")
            .build()
        val release = AppServices.http.newCall(request).await().use { r ->
            if (r.code == 404) throw IOException("That repository has no releases yet.")
            if (!r.isSuccessful) throw IOException("GitHub answered ${r.code}.")
            val text = withContext(Dispatchers.IO) { r.body.string() }
            NettleJson.camel.decodeFromString(GitHubRelease.serializer(), text)
        }
        val offer = UpdateCheck.pickApk(release)
        Log.info("update check: latest ${release.tagName}, this is ${BuildConfig.VERSION_NAME}, TV app file ${if (offer != null) "found" else "missing"}")
        return offer?.takeIf { UpdateCheck.isNewer(it.version, BuildConfig.VERSION_NAME) }
    }

    /**
     * Downloads the update into the app's cache and checks it is really a newer Nettle for TV.
     * [progress] gets 0..1 while downloading.
     */
    suspend fun download(update: AvailableUpdate, progress: (Float) -> Unit): File = withContext(Dispatchers.IO) {
        allowGitHub()
        val dir = File(AppServices.context.cacheDir, "updates").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() } // only ever keep the one being installed
        val file = File(dir, "Nettle-AndroidTV-${update.version}.apk")
        val request = Request.Builder().url(update.apkUrl).header("User-Agent", "Nettle").build()
        AppServices.http.newBuilder().callTimeout(java.time.Duration.ofMinutes(10)).readTimeout(java.time.Duration.ofSeconds(60)).build()
            .newCall(request).await().use { r ->
                if (!r.isSuccessful) throw IOException("Download failed (${r.code}).")
                val total = r.body.contentLength().takeIf { it > 0 } ?: update.size
                r.body.byteStream().use { input ->
                    file.outputStream().use { out ->
                        val buf = ByteArray(64 * 1024)
                        var done = 0L
                        while (true) {
                            val n = input.read(buf)
                            if (n < 0) break
                            out.write(buf, 0, n)
                            done += n
                            if (total > 0) progress((done.toFloat() / total).coerceIn(0f, 1f))
                        }
                    }
                }
            }
        if (update.size > 0 && file.length() != update.size) { file.delete(); throw IOException("The download was incomplete. Try again.") }

        // Must be this app, and newer than what's installed (Android also refuses a different signing key).
        val pm = AppServices.context.packageManager
        val info = pm.getPackageArchiveInfo(file.path, 0)
        if (info == null || info.packageName != AppServices.context.packageName) { file.delete(); throw IOException("That file isn't Nettle for Android TV.") }
        @Suppress("DEPRECATION")
        val newCode = if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong()
        if (newCode <= BuildConfig.VERSION_CODE) { file.delete(); throw IOException("The release's TV app (${info.versionName}) isn't newer than this one, so there's nothing to install.") }
        Log.info("update downloaded: ${info.versionName} (${file.length()} bytes)")
        file
    }

    /** Android needs the user's OK once before an app may install updates ("Install unknown apps"). */
    fun canInstall(): Boolean = AppServices.context.packageManager.canRequestPackageInstalls()

    /** Opens the TV's "Install unknown apps" switch for Nettle. False if this TV has no such screen. */
    fun openInstallPermission(): Boolean = start(
        Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${AppServices.context.packageName}"))
    )

    /** Hands the APK to Android's installer, which shows its own "update this app?" prompt. */
    fun install(apk: File): Boolean {
        val uri = FileProvider.getUriForFile(AppServices.context, "${AppServices.context.packageName}.updates", apk)
        return start(Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        })
    }

    private fun start(intent: Intent): Boolean = try {
        AppServices.context.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        true
    } catch (e: ActivityNotFoundException) {
        Log.warn("no screen for ${intent.action}: ${e.message}")
        false
    }
}
