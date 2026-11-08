package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.MediaStream
import io.github.devall96.nettle.core.models.NettleJson
import io.github.devall96.nettle.core.models.PlaybackInfoDto
import okhttp3.OkHttpClient
import org.junit.Assert.*
import org.junit.Test
import java.io.File

/**
 * Nettle's hardest rule: it never asks the server to transcode or remux. Kotlin twin of
 * tests/Nettle.Tests/NeverTranscodeTests.cs; these tests fail the build if anything in the
 * Android TV app starts requesting server-side conversion.
 */
class NeverTranscodeTests {
    private fun client() = JellyfinClient(OkHttpClient(), "test", "0").apply {
        configure("http://server:8096", "device", "secret-token-1234567890", "user")
    }

    @Test
    fun playbackInfoRequest_disablesEveryKindOfServerConversion() {
        val dto = PlaybackInfoDto()
        assertTrue(dto.enableDirectPlay)
        assertFalse(dto.enableDirectStream)
        assertFalse(dto.enableTranscoding)
        assertFalse(dto.allowVideoStreamCopy)
        assertFalse(dto.allowAudioStreamCopy)
        assertTrue(dto.deviceProfile.transcodingProfiles.isEmpty())
    }

    @Test
    fun playbackInfoRequest_serializesTheFlagsAsFalse() {
        val json = NettleJson.jellyfin.encodeToString(PlaybackInfoDto.serializer(), PlaybackInfoDto(userId = "u"))
        assertTrue(json, json.contains("\"EnableDirectPlay\":true"))
        assertTrue(json, json.contains("\"EnableTranscoding\":false"))
        assertTrue(json, json.contains("\"EnableDirectStream\":false"))
        assertTrue(json, json.contains("\"AllowVideoStreamCopy\":false"))
        assertTrue(json, json.contains("\"AllowAudioStreamCopy\":false"))
        assertTrue(json, json.contains("\"TranscodingProfiles\":[]"))
    }

    @Test
    fun streamUrl_isTheOriginalFile_withoutTheToken() {
        val url = client().directStreamUrl("item", "source", "session")
        assertTrue(url, url.startsWith("http://server:8096/Videos/item/stream?"))
        assertTrue(url, url.contains("static=true"))
        assertFalse(url, url.contains("m3u8", ignoreCase = true))
        assertFalse(url, url.contains("transcod", ignoreCase = true))
        // The token travels in the Authorization header, never in the URL.
        assertFalse(url, url.contains("secret-token"))
        assertFalse(url, url.contains("api_key", ignoreCase = true))
        assertFalse(url, url.contains("token", ignoreCase = true))
    }

    @Test
    fun subtitleAndImageUrls_carryNoToken() {
        val c = client()
        val sub = c.externalSubtitleUrl("item", "source", MediaStream(index = 3, codec = "subrip"))
        assertEquals("http://server:8096/Videos/item/source/Subtitles/3/0/Stream.srt", sub)
        for (u in listOf(sub, c.imageUrl("item", "Primary", "tag", 300)!!, c.trickplayTileUrl("item", "src", 320, 0)))
            assertFalse(u, u.contains("secret-token") || u.contains("api_key", ignoreCase = true))
    }

    /**
     * Scans all of the Android app's source for the ways a Jellyfin client asks for transcoded
     * or remuxed streams. Any hit is a broken promise to users.
     */
    @Test
    fun source_neverRequestsTranscodedStreams() {
        val root = File(System.getProperty("nettle.androidRoot") ?: "..")
        val forbidden = listOf(
            Regex("""master\.m3u8|main\.m3u8|/hls1?/""") to "HLS (transcoding) playlist",
            Regex("""(?i)transcodingUrl""") to "server transcoding URL",
            Regex("""(?i)enableTranscoding\s*(:\s*Boolean\s*)?=\s*true""") to "transcoding turned on",
            Regex("""(?i)enableDirectStream\s*(:\s*Boolean\s*)?=\s*true""") to "remuxing turned on",
            Regex("""(?i)allow(Video|Audio)StreamCopy\s*(:\s*Boolean\s*)?=\s*true""") to "remuxing turned on",
            Regex("""/Audio/[^"]*/universal""") to "universal (transcoding) audio endpoint",
            Regex("""(?i)"static"\s*,\s*false|static=false""") to "non-static (transcoded) stream",
            Regex("""androidx\.media3\.exoplayer\.(hls|dash|smoothstreaming)""") to "adaptive-streaming player module",
        )
        val sources = root.walkTopDown()
            .onEnter { it.name != "build" && it.name != ".gradle" && it.name != "test" }
            .filter { it.isFile && (it.extension == "kt" || it.extension == "java") && it.path.replace('\\', '/').contains("/src/main/") }
            .toList()
        assertTrue("no sources found under ${root.absolutePath}", sources.size > 5)
        val hits = sources.flatMap { f ->
            val text = f.readText()
            forbidden.flatMap { (re, why) -> re.findAll(text).map { "${f.relativeTo(root)}: $why ('${it.value}')" }.toList() }
        }
        assertTrue("Transcoding request found:\n" + hits.joinToString("\n"), hits.isEmpty())

        // Nor may the build pull in an adaptive-streaming (HLS / DASH / SmoothStreaming) player module.
        val catalog = File(root, "gradle/libs.versions.toml").readText()
        assertFalse("adaptive-streaming module in the version catalog", Regex("media3-exoplayer-(hls|dash|smoothstreaming)").containsMatchIn(catalog))
    }
}
