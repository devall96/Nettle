package io.github.devall96.nettle.core.services

import kotlin.math.min
import kotlin.math.pow

/**
 * How fast hold-to-seek moves (TV remote: holding Left / Right). It ramps with how long the button
 * has been held, not with how many key repeats arrived, so it stays controllable: 30× at first,
 * doubling every second, capped so a whole video takes about [CROSS_SECONDS] of holding.
 */
object ScrubSpeed {
    /** Video seconds per second held at the start. */
    const val START = 30.0
    /** Holding this long at full speed crosses the whole video. */
    const val CROSS_SECONDS = 8.0

    /** Video seconds per second of holding, after [heldMs] of holding, for a video [lengthMs] long. */
    fun rate(heldMs: Long, lengthMs: Long): Double {
        val cap = maxOf(START, lengthMs / 1000.0 / CROSS_SECONDS)
        return min(cap, START * 2.0.pow(heldMs.coerceAtLeast(0) / 1000.0))
    }

    /** How far (ms) to move for a key repeat [sinceLastMs] after the previous one, [heldMs] into the hold. */
    fun advanceMs(heldMs: Long, sinceLastMs: Long, lengthMs: Long): Long =
        (rate(heldMs, lengthMs) * sinceLastMs.coerceIn(0, 500)).toLong()
}
