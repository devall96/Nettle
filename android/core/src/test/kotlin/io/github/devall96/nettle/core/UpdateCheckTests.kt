package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.models.AppSettings
import io.github.devall96.nettle.core.models.NettleJson
import io.github.devall96.nettle.core.services.GitHubRelease
import io.github.devall96.nettle.core.services.UpdateCheck
import org.junit.Assert.*
import org.junit.Test

class UpdateCheckTests {
    @Test
    fun readsGitHubRepositoryFromSource() {
        assertEquals("devall96" to "Nettle", UpdateCheck.gitHubRepo("https://github.com/devall96/Nettle"))
        assertEquals("devall96" to "Nettle", UpdateCheck.gitHubRepo(" https://github.com/devall96/Nettle/ "))
        assertEquals("devall96" to "Nettle", UpdateCheck.gitHubRepo("https://github.com/devall96/Nettle.git"))
        assertEquals("devall96" to "Nettle", UpdateCheck.gitHubRepo("github.com/devall96/Nettle/releases"))
        assertNull(UpdateCheck.gitHubRepo("https://example.com/devall96/Nettle"))
        assertNull(UpdateCheck.gitHubRepo(""))
        assertEquals("https://api.github.com/repos/devall96/Nettle/releases/latest", UpdateCheck.latestReleaseApi(AppSettings.OFFICIAL_UPDATE_SOURCE))
    }

    @Test
    fun comparesVersions() {
        assertTrue(UpdateCheck.isNewer("v0.4.3", "0.4.2"))
        assertTrue(UpdateCheck.isNewer("v0.5.0", "0.4.9"))
        assertTrue(UpdateCheck.isNewer("v1.0.0", "0.10.0"))
        assertFalse(UpdateCheck.isNewer("v0.4.3", "0.4.3"))
        assertFalse(UpdateCheck.isNewer("v0.4.2", "0.4.3"))
        assertFalse(UpdateCheck.isNewer("nightly", "0.4.3"))
        assertEquals(listOf(0, 4, 3), UpdateCheck.parseVersion("Nettle 0.4.3 · parity hotfix"))
    }

    @Test
    fun picksTheTvAppFromTheLatestRelease() {
        val json = """{"tag_name":"v0.4.3","name":"Nettle 0.4.3","draft":false,"prerelease":false,"html_url":"https://github.com/devall96/Nettle/releases/tag/v0.4.3",
            "assets":[{"name":"Nettle-win-Setup.exe","size":1,"browser_download_url":"https://x/setup"},
                      {"name":"Nettle-AndroidTV.apk","size":5360750,"browser_download_url":"https://github.com/devall96/Nettle/releases/download/v0.4.3/Nettle-AndroidTV.apk"}],
            "author":{"login":"devall96"}}"""
        val release = NettleJson.camel.decodeFromString(GitHubRelease.serializer(), json)
        val u = UpdateCheck.pickApk(release)!!
        assertEquals("0.4.3", u.version)
        assertEquals(5360750L, u.size)
        assertTrue(u.apkUrl.endsWith("/v0.4.3/Nettle-AndroidTV.apk"))

        assertNull(UpdateCheck.pickApk(release.copy(assets = release.assets.take(1))))
        assertNull(UpdateCheck.pickApk(release.copy(prerelease = true)))
    }

    @Test
    fun updateSettingsDefaultLikeDesktop() {
        val s = NettleJson.files.decodeFromString(AppSettings.serializer(), """{"theme":"midnight"}""")
        assertEquals(AppSettings.OFFICIAL_UPDATE_SOURCE, s.updateSource)
        assertFalse(s.autoCheckUpdates)
    }
}
