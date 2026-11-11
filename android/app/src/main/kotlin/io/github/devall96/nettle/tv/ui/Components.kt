package io.github.devall96.nettle.tv.ui

import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.Image
import io.github.devall96.nettle.tv.theme.Nettle

/** The Button classes from Controls.axaml. */
enum class ButtonKind { Default, Primary, Ghost, Glass, Icon, Chip, Nav }

/** Plain text in the theme's colour and body style (Avalonia's TextBlock). */
@Composable
fun T(
    text: String,
    modifier: Modifier = Modifier,
    style: TextStyle = Nettle.type.body,
    color: Color = Color.Unspecified,
    size: TextUnit = TextUnit.Unspecified,
    weight: FontWeight? = null,
    maxLines: Int = Int.MAX_VALUE,
    lineHeight: TextUnit = TextUnit.Unspecified,
    muted: Boolean = false,
    italic: Boolean = false,
    align: androidx.compose.ui.text.style.TextAlign? = null,
) {
    val c = when {
        color != Color.Unspecified -> color
        muted -> Nettle.colors.textMuted
        style.color != Color.Unspecified -> style.color
        else -> Nettle.colors.text
    }
    BasicText(
        text = text,
        modifier = modifier,
        style = style.merge(
            TextStyle(
                color = c,
                fontSize = size,
                fontWeight = weight,
                lineHeight = lineHeight,
                fontStyle = if (italic) androidx.compose.ui.text.font.FontStyle.Italic else null,
                textAlign = align ?: androidx.compose.ui.text.style.TextAlign.Unspecified,
            ),
        ),
        maxLines = maxLines,
        overflow = TextOverflow.Ellipsis,
    )
}

@Composable
fun Icon(icon: ImageVector, modifier: Modifier = Modifier, size: Dp = 22.dp, tint: Color = Nettle.colors.text) {
    Image(icon, null, modifier.size(size), colorFilter = ColorFilter.tint(tint))
}

/**
 * Nettle's button: radius 12, 2-unit border that turns Accent when focused (Avalonia's
 * :focus-visible), SemiBold text; variants as in Controls.axaml.
 */
@Composable
fun NButton(
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    kind: ButtonKind = ButtonKind.Default,
    selected: Boolean = false,
    enabled: Boolean = true,
    padding: PaddingValues? = null,
    background: Brush? = null,
    focusRequester: androidx.compose.ui.focus.FocusRequester? = null,
    onFocus: (() -> Unit)? = null,
    content: @Composable RowScope.(contentColor: Color) -> Unit,
) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val pressed by source.collectIsPressedAsState()
    LaunchedEffect(focused) { if (focused) onFocus?.invoke() }

    val shape = when (kind) {
        ButtonKind.Icon, ButtonKind.Chip -> CircleShape
        else -> RoundedCornerShape(12.dp)
    }
    val bg: Brush = when {
        !enabled -> SolidColor(p.surface)
        background != null -> background
        kind == ButtonKind.Primary -> p.accentGradient
        kind == ButtonKind.Ghost || kind == ButtonKind.Icon -> SolidColor(Color.Transparent)
        // Glass sits on artwork: a light wash on dark themes, a dark one on light themes (white on the Daylight fade was unreadable).
        kind == ButtonKind.Glass -> SolidColor(if (p.isLight) Color(0x26000000) else Color(0x33FFFFFF))
        kind == ButtonKind.Chip -> SolidColor(if (selected) p.accent else p.surface)
        kind == ButtonKind.Nav -> SolidColor(if (selected) p.accentSoft else Color.Transparent)
        else -> SolidColor(p.surfaceHigh)
    }
    val fg = when {
        !enabled -> p.textMuted
        kind == ButtonKind.Primary -> p.onAccent
        kind == ButtonKind.Glass -> if (p.isLight) p.text else Color.White
        kind == ButtonKind.Chip && selected -> p.onAccent
        kind == ButtonKind.Nav -> if (selected) p.accent else if (focused) p.text else p.textMuted
        else -> p.text
    }
    val border = when {
        !focused -> Color.Transparent
        kind == ButtonKind.Primary -> p.text
        else -> p.accent
    }
    val pad = padding ?: when (kind) {
        ButtonKind.Primary -> PaddingValues(26.dp, 12.dp)
        ButtonKind.Icon -> PaddingValues(10.dp)
        ButtonKind.Chip -> PaddingValues(16.dp, 8.dp)
        ButtonKind.Nav -> PaddingValues(14.dp, 11.dp)
        else -> PaddingValues(18.dp, 10.dp)
    }
    val scale by animateFloatAsState(if (pressed && Nettle.motion) 0.97f else 1f, tween(160, easing = CubicOut), label = "press")
    val textStyle = Nettle.type.body.copy(
        fontWeight = FontWeight.SemiBold, color = fg,
        fontSize = if (kind == ButtonKind.Primary) 15.sp else 14.sp,
    )
    Row(
        modifier
            .graphicsLayer { scaleX = scale; scaleY = scale }
            .clip(shape)
            .background(bg, shape)
            .border(2.dp, border, shape)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick)
            .padding(pad),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = if (kind == ButtonKind.Nav) Arrangement.Start else Arrangement.Center,
    ) {
        CompositionLocalProvider(LocalTextStyleOverride provides textStyle) { content(fg) }
    }
}

/** The text style a button gives its content (Avalonia's inherited Foreground / FontWeight). */
val LocalTextStyleOverride = compositionLocalOf<TextStyle?> { null }

/** Text inside a button, picking up the button's colour and weight. */
@Composable
fun BT(text: String, modifier: Modifier = Modifier, color: Color? = null, size: TextUnit = TextUnit.Unspecified, maxLines: Int = 1) {
    val s = LocalTextStyleOverride.current ?: Nettle.type.body
    BasicText(text, modifier, s.merge(TextStyle(color = color ?: s.color, fontSize = size)), maxLines = maxLines, overflow = TextOverflow.Ellipsis)
}

/** Shorthand for a button with just a label. */
@Composable
fun TextButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, kind: ButtonKind = ButtonKind.Default,
               selected: Boolean = false, enabled: Boolean = true, padding: PaddingValues? = null,
               focusRequester: androidx.compose.ui.focus.FocusRequester? = null) =
    NButton(onClick, modifier, kind, selected, enabled, padding, focusRequester = focusRequester) { BT(text) }

/** Round icon button (Button.icon), optionally "active" (accent icon), optionally with a fill. */
@Composable
fun IconButton(icon: ImageVector, onClick: () -> Unit, modifier: Modifier = Modifier, active: Boolean = false,
               glass: Boolean = false, background: Color? = null, size: Dp = 22.dp, padding: Dp = 10.dp, enabled: Boolean = true,
               focusRequester: androidx.compose.ui.focus.FocusRequester? = null) =
    NButton(onClick, modifier, if (glass) ButtonKind.Glass else ButtonKind.Icon, enabled = enabled,
        padding = PaddingValues(padding), background = background?.let { SolidColor(it) },
        focusRequester = focusRequester) { fg ->
        Icon(icon, size = size, tint = if (active) Nettle.colors.accent else fg)
    }

/** Primary button with a play icon and a label. */
@Composable
fun PlayButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, iconSize: Dp = 18.dp,
               focusRequester: androidx.compose.ui.focus.FocusRequester? = null) =
    NButton(onClick, modifier, ButtonKind.Primary, focusRequester = focusRequester) { fg ->
        Icon(io.github.devall96.nettle.tv.theme.Icons.Play, size = iconSize, tint = fg)
        Spacer(Modifier.width(10.dp))
        BT(text)
    }

val CubicOut = CubicBezierEasing(0.215f, 0.61f, 0.355f, 1f)

/** Arc.spin: 260° accent arc, 4 thick, round caps, one turn every 0.9 s. */
@Composable
fun Spinner(modifier: Modifier = Modifier, size: Dp = 44.dp) {
    val color = Nettle.colors.accent
    val t = rememberInfiniteTransition(label = "spin")
    val angle by t.animateFloat(0f, 360f, infiniteRepeatable(tween(900, easing = LinearEasing)), label = "angle")
    Canvas(modifier.size(size)) {
        val sw = 4.dp.toPx()
        rotate(angle) {
            drawArc(color, -90f, 260f, false, Offset(sw / 2, sw / 2), Size(this.size.width - sw, this.size.height - sw), style = Stroke(sw, cap = StrokeCap.Round))
        }
    }
}

/** ProgressBar.thin: 4 tall, radius 2, accent on a #40FFFFFF track. */
@Composable
fun ThinProgress(value: Float, modifier: Modifier = Modifier) {
    val accent = Nettle.colors.accent
    Box(
        modifier.height(4.dp).clip(RoundedCornerShape(2.dp)).background(Color(0x40FFFFFF))
            .drawBehind { drawRect(accent, size = Size(size.width * value.coerceIn(0f, 1f), size.height)) },
    )
}

/** Border.panel: Surface, radius 18, padding 24, 1-unit border. */
@Composable
fun Panel(modifier: Modifier = Modifier, padding: PaddingValues = PaddingValues(24.dp), content: @Composable ColumnScope.() -> Unit) {
    val p = Nettle.colors
    Column(
        modifier.clip(RoundedCornerShape(18.dp)).background(p.surface).border(1.dp, p.border, RoundedCornerShape(18.dp)).padding(padding),
        content = content,
    )
}

/** TextBlock.section: 13, Bold, 1.2 tracking, muted. */
@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier) = T(text, modifier, style = Nettle.type.section, muted = true)

/** The red error text used across pages. */
@Composable
fun ErrorText(text: String?, modifier: Modifier = Modifier) {
    if (!text.isNullOrEmpty()) T(text, modifier, color = Nettle.colors.danger)
}

/** Fluent ToggleSwitch look (40 × 20), in the theme's accent. */
@Composable
fun ToggleKnob(on: Boolean, modifier: Modifier = Modifier) {
    val p = Nettle.colors
    val x by animateDpAsState(if (on) 24.dp else 4.dp, tween(150), label = "knob")
    Box(
        modifier.size(40.dp, 20.dp).clip(CircleShape)
            .background(if (on) p.accent else Color.Transparent)
            .border(1.dp, if (on) p.accent else p.textMuted, CircleShape),
    ) {
        Box(Modifier.offset(x = x, y = 4.dp).size(12.dp).clip(CircleShape).background(if (on) p.onAccent else p.textMuted))
    }
}

/** Fluent CheckBox look (20 × 20, radius 4). */
@Composable
fun CheckMark(checked: Boolean, enabled: Boolean = true, modifier: Modifier = Modifier) {
    val p = Nettle.colors
    Box(
        modifier.size(20.dp).clip(RoundedCornerShape(4.dp))
            .background(if (checked) (if (enabled) p.accent else p.textMuted) else Color.Transparent)
            .border(1.dp, if (checked) Color.Transparent else p.textMuted, RoundedCornerShape(4.dp)),
        contentAlignment = Alignment.Center,
    ) {
        if (checked) Icon(io.github.devall96.nettle.tv.theme.Icons.Check, size = 14.dp, tint = p.onAccent)
    }
}
