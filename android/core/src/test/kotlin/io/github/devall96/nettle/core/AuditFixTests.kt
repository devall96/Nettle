package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.api.SeerrClient
import io.github.devall96.nettle.core.api.extinfTitle
import io.github.devall96.nettle.core.models.MediaStream
import io.github.devall96.nettle.core.models.VideoFormat
import org.junit.Test
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull

/** Fixes from the 0.5.1 audit (Android TV side; the desktop ones are in tests/Nettle.Tests/AuditFixTests.cs). */
class AuditFixTests {
    @Test fun channelNamesMayContainCommas() {
        assertEquals("News, Weather & Sport", extinfTitle("#EXTINF:-1 tvg-id=\"a\",News, Weather & Sport"))
        assertEquals("Cartoons", extinfTitle("#EXTINF:-1 tvg-id=\"a\" group-title=\"Kids, Family\",Cartoons"))
        assertNull(extinfTitle("#EXTINF:-1 tvg-id=\"a\""))
    }

    @Test fun dtsLabelsComeFromTheRealProfile() {
        fun label(title: String, profile: String) = VideoFormat.audioLabel(MediaStream(type = "Audio", codec = "dts", displayTitle = title, profile = profile))
        assertEquals("DTS", label("German - DTS - 5.1", "DTS"))
        assertEquals("DTS-HD MA", label("English - DTS-HD MA - 7.1", "DTS-HD MA"))
        assertEquals("DTS-HD", label("English - DTS-HD HRA - 5.1", "DTS-HD HRA"))
    }

    @Test fun hdrLabelsCoverNewerDolbyVisionTypes() {
        assertEquals("Dolby Vision", VideoFormat.hdrLabel("DOVIWithEL").first)
        assertEquals("Dolby Vision", VideoFormat.hdrLabel("DOVIWithELHDR10Plus").first)
        assertNull(VideoFormat.hdrLabel("DOVIInvalid").first)
    }

    @Test fun seerrSignInsArePerProfile() {
        assertEquals("seerr.cookie.abc", SeerrClient.cookieSecret("abc"))
        assertNotEquals(SeerrClient.cookieSecret("abc"), SeerrClient.cookieSecret("def"))
        assertEquals(SeerrClient.LEGACY_COOKIE, SeerrClient.cookieSecret(null))
    }
}
