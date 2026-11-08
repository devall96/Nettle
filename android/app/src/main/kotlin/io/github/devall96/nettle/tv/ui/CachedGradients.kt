package io.github.devall96.nettle.tv.ui

import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Canvas
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.drawscope.CanvasDrawScope
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import kotlin.math.ceil
import kotlin.math.roundToInt

/**
 * Paints [brushes] (in order, each over the last) like stacked `background(brush)` layers, but
 * renders them ONCE into a small bitmap ([downscale]× smaller) that is then stretched with
 * bilinear filtering. Smooth gradients look the same, but the TV's small GPU samples one texture
 * instead of evaluating several full-screen gradient shaders every frame (the costliest thing
 * Nettle drew on the onn: ~30 ms per frame for the two backdrop fades alone).
 */
fun Modifier.cachedGradients(vararg brushes: Brush, downscale: Int = 4): Modifier = drawWithCache {
    val w = ceil(size.width / downscale).toInt().coerceAtLeast(2)
    val h = ceil(size.height / downscale).toInt().coerceAtLeast(2)
    val bitmap = ImageBitmap(w, h)
    CanvasDrawScope().draw(this, layoutDirection, Canvas(bitmap), Size(w.toFloat(), h.toFloat())) {
        for (b in brushes) drawRect(b)
    }
    val dst = IntSize(size.width.roundToInt(), size.height.roundToInt())
    onDrawBehind {
        drawImage(bitmap, IntOffset.Zero, IntSize(w, h), IntOffset.Zero, dst, filterQuality = FilterQuality.Low)
    }
}
