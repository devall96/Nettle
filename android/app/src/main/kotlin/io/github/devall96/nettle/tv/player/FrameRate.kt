package io.github.devall96.nettle.tv.player

import android.app.Activity
import android.view.Display
import io.github.devall96.nettle.core.models.FrameRateMatching
import io.github.devall96.nettle.core.services.Log
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * Switches the TV's output to suit the video (own implementation): the refresh rate that shows
 * the film's frame rate without judder (23.976 → 23.976 Hz, 25 → 50 Hz…), and optionally the
 * resolution too. Uses the window's preferred display mode, so it works on every Android TV
 * version; the original mode comes back when the player closes.
 */
object FrameRate {
    private var original: Int? = null

    fun apply(activity: Activity, fps: Float, videoWidth: Int, videoHeight: Int, mode: FrameRateMatching) {
        if (mode == FrameRateMatching.Off || fps <= 1f) { Log.info("display mode: no switch (matching $mode, video ${"%.3f".format(fps)} fps)"); return }
        @Suppress("DEPRECATION") val display = activity.windowManager.defaultDisplay ?: return
        val current = display.mode
        val target = pick(display.supportedModes.toList(), current, fps, videoWidth, videoHeight, mode == FrameRateMatching.RefreshRateAndResolution) ?: return
        if (target.modeId == current.modeId) { Log.info("display mode: already ${"%.3f".format(current.refreshRate)} Hz for ${"%.3f".format(fps)} fps"); return }
        if (original == null) original = activity.window.attributes.preferredDisplayModeId
        Log.info("display mode: ${current.physicalWidth}x${current.physicalHeight}@${"%.3f".format(current.refreshRate)} → " +
            "${target.physicalWidth}x${target.physicalHeight}@${"%.3f".format(target.refreshRate)} for ${"%.3f".format(fps)} fps")
        val attrs = activity.window.attributes
        attrs.preferredDisplayModeId = target.modeId
        activity.window.attributes = attrs
    }

    fun restore(activity: Activity) {
        val o = original ?: return
        original = null
        val attrs = activity.window.attributes
        attrs.preferredDisplayModeId = o
        activity.window.attributes = attrs
    }

    /** A refresh rate is judder-free for [fps] when it's the same rate or a whole multiple of it. */
    private fun cadence(rate: Float, fps: Float): Int? {
        val ratio = rate / fps
        val n = ratio.roundToInt()
        return if (n >= 1 && abs(ratio - n) < 0.01f) n else null
    }

    fun pick(modes: List<Display.Mode>, current: Display.Mode, fps: Float, w: Int, h: Int, matchResolution: Boolean): Display.Mode? {
        val sizeModes = if (matchResolution && w > 0) {
            // The smallest output that still holds the video (a 1080p film → 1080p, 4K → 4K).
            val fits = modes.filter { it.physicalWidth >= w * 0.95 || it.physicalHeight >= h * 0.95 }
            val best = fits.minByOrNull { it.physicalWidth * it.physicalHeight } ?: modes.maxByOrNull { it.physicalWidth * it.physicalHeight } ?: return null
            modes.filter { it.physicalWidth == best.physicalWidth && it.physicalHeight == best.physicalHeight }
        } else modes.filter { it.physicalWidth == current.physicalWidth && it.physicalHeight == current.physicalHeight }
        // Exact rate first, then the lowest whole multiple.
        return sizeModes.mapNotNull { m -> cadence(m.refreshRate, fps)?.let { n -> m to n } }
            .sortedWith(compareBy({ it.second }, { abs(it.first.refreshRate - fps * it.second) }))
            .firstOrNull()?.first
    }
}
