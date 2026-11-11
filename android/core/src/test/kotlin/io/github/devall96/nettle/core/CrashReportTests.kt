package io.github.devall96.nettle.core

import io.github.devall96.nettle.core.api.Query
import io.github.devall96.nettle.core.services.CrashReport
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** 1.0.1: the Android 9 to 12 crash (query escaping), and the "closed unexpectedly" report. */
class CrashReportTests {
    @Test fun queryEscapingMatchesUriEscapeDataString() {
        assertEquals("a%20b%2Bc%26d%3De~f%2A", Query.escape("a b+c&d=e~f*"))
        assertEquals("%C3%A9%E2%82%AC", Query.escape("é€"))
        assertEquals("?SearchTerm=night%20of&Limit=5", Query().add("SearchTerm", "night of").add("Limit", 5).toString())
    }

    @Test fun reportNamesTheErrorItsCauseAndWhereItHappened() {
        val cause = IllegalArgumentException("bad value")
        val text = CrashReport.format(IllegalStateException("it broke", cause), "Nettle 1.0.1 · Android 9", "2026-11-11 07:00")
        val lines = text.lines()
        assertEquals("Nettle 1.0.1 · Android 9", lines[0])
        assertEquals("2026-11-11 07:00", lines[1])
        assertTrue(text.contains("java.lang.IllegalStateException: it broke"))
        assertTrue(text.contains("Caused by: java.lang.IllegalArgumentException: bad value"))
        assertTrue(text.contains("  at core.CrashReportTests.reportNamesTheErrorItsCauseAndWhereItHappened (CrashReportTests.kt:"))
    }

    @Test fun reportMasksTokensAndStaysShort() {
        val text = CrashReport.format(RuntimeException("GET /Items?api_key=abcdef1234567890 failed"), "h", "t", maxFrames = 3)
        assertFalse(text.contains("abcdef1234567890"))
        assertTrue(text.contains("api_key=***"))
        assertTrue(text.lines().count { it.startsWith("  at ") } <= 3)
    }

    @Test fun reportSurvivesAnErrorThatIsItsOwnCause() {
        val e = object : RuntimeException("loop") { override val cause: Throwable get() = this }
        assertTrue(CrashReport.format(e, "h", "t").contains("loop"))
    }
}
