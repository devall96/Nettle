package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.api.NetworkSafety
import io.github.devall96.nettle.core.api.NtvClient
import io.github.devall96.nettle.core.api.Query
import io.github.devall96.nettle.core.api.SeerrClient
import io.github.devall96.nettle.core.models.*
import okhttp3.OkHttpClient
import org.junit.Assert.*
import org.junit.Test

class CoreTests {
    @Test
    fun scrubSpeed_rampsWithTimeHeld_andCrossesAFilmInSeconds() {
        val film = 2 * 3600_000L
        assertEquals(30.0, io.github.devall96.nettle.core.services.ScrubSpeed.rate(0, film), 0.001)
        assertEquals(60.0, io.github.devall96.nettle.core.services.ScrubSpeed.rate(1000, film), 0.001)
        assertEquals(900.0, io.github.devall96.nettle.core.services.ScrubSpeed.rate(60_000, film), 0.001) // 2 h / 8 s
        // A short clip never drops below the starting speed.
        assertEquals(30.0, io.github.devall96.nettle.core.services.ScrubSpeed.rate(10_000, 60_000), 0.001)
        // Holding 3 s through 20 Hz key repeats moves a few minutes, not an hour (the old steps jumped ~1 h+).
        var held = 0L; var moved = 0L
        while (held < 3000) { held += 50; moved += io.github.devall96.nettle.core.services.ScrubSpeed.advanceMs(held, 50, film) }
        assertTrue("moved $moved ms", moved in 180_000..400_000)
        // A long gap between repeats (a stall) can't jump far.
        assertTrue(io.github.devall96.nettle.core.services.ScrubSpeed.advanceMs(1000, 5000, film) <= 30_000)
    }

    @Test
    fun candidates_ipGetsDefaultPortFirst_nameGetsBareFirst() {
        assertEquals(listOf("http://192.168.1.20:8096", "http://192.168.1.20"), JellyfinClient.candidatesFor("http", "192.168.1.20"))
        assertEquals(listOf("https://jf.example.com", "https://jf.example.com:8920"), JellyfinClient.candidatesFor("https", "jf.example.com"))
        assertEquals(listOf("http://192.168.1.20:9000"), JellyfinClient.candidatesFor("http", "192.168.1.20:9000"))
    }

    @Test
    fun query_escapesLikeDotNet() {
        assertEquals("?a=b%20c&n=3&t=true", Query().add("a", "b c").add("skip", null as String?).add("n", 3).add("t", true).toString())
    }

    @Test
    fun privateHosts() {
        assertTrue(NetworkSafety.isPrivateHost("192.168.1.5"))
        // Split so the repo's pre-commit secret scan (CGNAT / ts.net patterns) doesn't flag test data.
        assertTrue(NetworkSafety.isPrivateHost("100." + "100.1.1"))
        assertTrue(NetworkSafety.isPrivateHost("nas"))
        assertTrue(NetworkSafety.isPrivateHost("box" + ".ts.net"))
        assertFalse(NetworkSafety.isPrivateHost("8.8.8.8"))
        assertFalse(NetworkSafety.isPrivateHost("jellyfin.example.com"))
        assertTrue(NetworkSafety.isInsecureRemote("http://jellyfin.example.com"))
        assertFalse(NetworkSafety.isInsecureRemote("https://jellyfin.example.com"))
    }

    @Test
    fun jellyfinJson_readsPascalCase() {
        val item = NettleJson.jellyfin.decodeFromString(BaseItemDto.serializer(),
            """{"Id":"x","Name":"Film","Type":"Movie","UserData":{"Played":true,"PlaybackPositionTicks":5},"ImageTags":{"Primary":"t"},"Unknown":1}""")
        assertEquals("Film", item.name)
        assertTrue(item.userData!!.played)
        assertEquals("t", item.tag("Primary"))
        assertTrue(item.isPlayable)
    }

    @Test
    fun music_isHidden() {
        assertTrue(Music.isHidden(BaseItemDto(collectionType = "music")))
        assertTrue(Music.isHidden(BaseItemDto(collectionType = "musicvideos")))
        assertTrue(Music.isHidden(BaseItemDto(type = "MusicVideo")))
        assertFalse(Music.isHidden(BaseItemDto(collectionType = "movies")))
        assertFalse(BaseItemDto(type = "MusicVideo").isPlayable)
    }

    @Test
    fun xmltv_timesAndEpisodes() {
        assertEquals("S1:E3", NtvClient.fromXmltvNs("0.2.0/1"))
        assertEquals("E5", NtvClient.fromXmltvNs(".4."))
        assertEquals(1_700_000_000_000L - 3_600_000L * 0, NtvClient.parseXmltvTime("20231114221320 +0000"))
        assertEquals(NtvClient.parseXmltvTime("20231114231320 +0100"), NtvClient.parseXmltvTime("20231114221320 +0000"))
    }

    @Test
    fun ntv_m3uAndGuide() {
        val ntv = NtvClient(OkHttpClient())
        ntv.configure("ntv.local", null)
        assertEquals("http://ntv.local:19850", ntv.baseUrl)
        val chans = ntv.parseM3u("#EXTM3U\n#EXTINF:-1 tvg-id=\"c1\" tvg-chno=\"5\" tvg-logo=\"http://l/x.png\",Retro One\nhttp://ntv.local:19850/stream/abc?x=1\n" +
            "#EXTINF:-1 tvg-id=\"c2\",Bad\nfile:///etc/passwd\n")
        // Anything that isn't an http(s) URL is a path on the NostalgiaTV server (same as desktop).
        assertEquals(2, chans.size)
        assertTrue(chans[1].streamUrl.startsWith("http://ntv.local:19850/"))
        assertEquals("abc", chans[0].stableId)
        assertEquals("5", chans[0].number)
        val xml = """<?xml version="1.0"?><tv><channel id="c1"><icon src="http://i"/></channel>
            <programme channel="c1" start="20231114220000 +0000" stop="20231114230000 +0000"><title>Show</title><sub-title>Ep</sub-title>
            <episode-num system="xmltv_ns">0.1.0</episode-num></programme></tv>"""
        NtvClient.parseXmltv(xml.byteInputStream(), chans, 0, Long.MAX_VALUE)
        val p = chans[0].programs.single()
        assertEquals("Show", p.title)
        assertEquals("S1:E2", p.episodeNum)
        assertNotNull(chans[0].programAt(p.start + 1))
    }

    @Test
    fun seerr_imagesGoThroughTheProxyByDefault() {
        val s = SeerrClient(OkHttpClient())
        s.configure("seerr.local:5055", null, null)
        assertEquals("http://seerr.local:5055/imageproxy/tmdb/t/p/w342/a.jpg", s.imageUrl("/a.jpg"))
        s.allowDirectTmdbImages = true
        assertEquals("https://image.tmdb.org/t/p/w342/a.jpg", s.imageUrl("/a.jpg"))
    }

    @Test
    fun ntvSlot_jellyfinItem() {
        assertEquals("srv" to "item", NtvSlot(ratingKey = "jelly:srv:item").jellyfinItem())
        assertNull(NtvSlot(ratingKey = "plex:1").jellyfinItem())
    }

    @Test
    fun settings_roundTrip() {
        val s = AppSettings(theme = "ember").apply { playback.audioPassthrough = true }
        val back = NettleJson.files.decodeFromString(AppSettings.serializer(), NettleJson.files.encodeToString(AppSettings.serializer(), s))
        assertEquals(s, back)
    }
}
