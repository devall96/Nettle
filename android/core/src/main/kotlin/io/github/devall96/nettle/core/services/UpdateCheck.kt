package io.github.devall96.nettle.core.services

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * The pure parts of the Android TV updater (no network, no Android): which repository to ask,
 * which file in its latest release is the TV app, and whether that release is newer.
 * Same source and rules as the Windows / Linux updater (Settings → Updates).
 */
object UpdateCheck {
    /** The file every Nettle release carries for Android TV. */
    const val APK_NAME = "Nettle-AndroidTV.apk"

    /** "https://github.com/owner/repo" (or with .git / a trailing path) → owner to repo; null if it isn't a GitHub repository. */
    fun gitHubRepo(source: String?): Pair<String, String>? {
        val s = source?.trim()?.removeSuffix("/") ?: return null
        val m = Regex("""^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+?)(?:\.git)?(?:/.*)?$""", RegexOption.IGNORE_CASE)
            .matchEntire(s) ?: return null
        return m.groupValues[1] to m.groupValues[2]
    }

    /** GitHub's "latest release" API address for the source (null if it isn't a GitHub repository). */
    fun latestReleaseApi(source: String?): String? =
        gitHubRepo(source)?.let { (owner, repo) -> "https://api.github.com/repos/$owner/$repo/releases/latest" }

    /** Version numbers like "v0.4.3" or "0.4.3 · parity hotfix" → [0, 4, 3]; null if there's none. */
    fun parseVersion(text: String?): List<Int>? {
        val m = Regex("""(\d+)\.(\d+)(?:\.(\d+))?""").find(text ?: return null) ?: return null
        return listOf(m.groupValues[1].toInt(), m.groupValues[2].toInt(), m.groupValues[3].ifEmpty { "0" }.toInt())
    }

    /** True when [candidate] is a higher version than [current]. */
    fun isNewer(candidate: String?, current: String?): Boolean {
        val a = parseVersion(candidate) ?: return false
        val b = parseVersion(current) ?: return true
        for (i in 0..2) if (a[i] != b[i]) return a[i] > b[i]
        return false
    }

    /** What the latest release offers for Android TV, or null when it has no TV app file. */
    fun pickApk(release: GitHubRelease): AvailableUpdate? {
        if (release.draft || release.prerelease) return null
        val asset = release.assets.firstOrNull { it.name.equals(APK_NAME, ignoreCase = true) } ?: return null
        val version = parseVersion(release.tagName)?.joinToString(".") ?: return null
        return AvailableUpdate(version, release.name ?: release.tagName, asset.browserDownloadUrl, asset.size, release.htmlUrl)
    }
}

data class AvailableUpdate(val version: String, val title: String, val apkUrl: String, val size: Long, val pageUrl: String?)

@Serializable
data class GitHubRelease(
    @SerialName("tag_name") val tagName: String = "",
    val name: String? = null,
    val draft: Boolean = false,
    val prerelease: Boolean = false,
    @SerialName("html_url") val htmlUrl: String? = null,
    val assets: List<GitHubAsset> = emptyList(),
)

@Serializable
data class GitHubAsset(
    val name: String = "",
    val size: Long = 0,
    @SerialName("browser_download_url") val browserDownloadUrl: String = "",
)
