package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.api.JellyfinClient
import io.github.devall96.nettle.core.models.*
import io.github.devall96.nettle.core.services.SyncPlayService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.serialization.json.Json
import okhttp3.OkHttpClient
import org.junit.Assert.*
import org.junit.Test

/** Watch together (SyncPlay), Android TV side; the desktop has the same tests in tests/Nettle.Tests/SyncPlayTests.cs. */
class SyncPlayTests {
    private fun j(s: String) = Json.parseToJsonElement(s)
    private fun service() = SyncPlayService(JellyfinClient(OkHttpClient(), "test", "0"), CoroutineScope(Dispatchers.Unconfined))

    @Test fun clockOffsetAndRoundTrip() {
        val t0 = 1_000_000L
        val t1 = t0 + 30 - 500
        val t2 = t1 + 2
        val t3 = t0 + 62
        val (offset, rtt) = SyncPlayMath.clockSample(t0, t1, t2, t3)
        assertEquals(-500L, offset)
        assertEquals(60L, rtt)
    }

    @Test fun driftCorrection() {
        assertEquals(SyncCorrectionKind.None, SyncPlayMath.correction(10_000.0, 10_050.0).kind)
        assertEquals(SyncCorrectionKind.Speed, SyncPlayMath.correction(10_000.0, 10_600.0).kind)
        assertEquals(SyncCorrectionKind.Skip, SyncPlayMath.correction(10_000.0, 13_000.0).kind)
        val fix = SyncPlayMath.correction(10_000.0, 10_600.0)
        assertEquals(600.0, (fix.speed - 1) * fix.durationMs, 0.001)
    }

    @Test fun serviceFollowsTheGroup() {
        val s = service()
        var queued: SyncPlayQueue? = null
        var command: SyncPlayCommand? = null
        s.onQueue = { queued = it }
        s.onCommand = { command = it }
        assertTrue(s.handle("SyncPlayGroupUpdate", j("""{"GroupId":"g1","Type":"GroupJoined","Data":{"GroupId":"g1","GroupName":"Movie night","State":"Idle","Participants":["sam"]}}""")))
        assertEquals("Movie night", s.group?.name)
        s.handle("SyncPlayGroupUpdate", j("""{"GroupId":"g1","Type":"UserJoined","Data":"alex"}"""))
        assertEquals(listOf("sam", "alex"), s.group?.participants)
        s.handle("SyncPlayGroupUpdate", j("""{"GroupId":"g1","Type":"PlayQueue","Data":{"Reason":"NewPlaylist","Playlist":[{"ItemId":"i1","PlaylistItemId":"e1"},{"ItemId":"i2","PlaylistItemId":"e2"}],"PlayingItemIndex":1,"StartPositionTicks":50000000,"IsPlaying":false}}"""))
        assertEquals("e2", s.currentPlaylistItemId)
        assertEquals(50_000_000L, queued?.startPositionTicks)
        s.handle("SyncPlayCommand", j("""{"GroupId":"g1","PlaylistItemId":"e2","When":"2026-11-11T05:00:00.5Z","PositionTicks":50000000,"Command":"Unpause"}"""))
        assertEquals("Unpause", command?.command)
        assertEquals(java.time.Instant.parse("2026-11-11T05:00:00.5Z").toEpochMilli(), command?.whenMs)
        s.handle("SyncPlayGroupUpdate", j("""{"GroupId":"g1","Type":"GroupLeft","Data":"g1"}"""))
        assertFalse(s.inGroup)
        assertFalse(s.handle("UserDataChanged", j("{}")))
    }

    @Test fun timesWithoutAZoneAreUtc() {
        assertEquals(java.time.Instant.parse("2026-11-11T05:00:00Z").toEpochMilli(), SyncPlayJson.parseTime("2026-11-11T05:00:00"))
        assertEquals("2026-11-11T05:00:00.500Z", SyncPlayJson.format(java.time.Instant.parse("2026-11-11T05:00:00.5Z").toEpochMilli()))
    }
}
