package io.github.devall96.nettle.tv.ui

import android.view.KeyEvent as AndroidKey
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.BringIntoViewSpec
import androidx.compose.foundation.gestures.LocalBringIntoViewSpec
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.rememberVectorPainter
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.drawscope.clipPath
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.input.key.*
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil3.compose.AsyncImage
import coil3.request.ImageRequest
import coil3.request.crossfade
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.R
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.vm.CardRow
import io.github.devall96.nettle.tv.vm.MediaCard
import io.github.devall96.nettle.tv.vm.Page

/** An image from one of the user's servers, fading in when loaded (Image.fade). */
@Composable
fun NetImage(url: String?, modifier: Modifier = Modifier, contentScale: ContentScale = ContentScale.Crop, alignment: Alignment = Alignment.Center,
             onLoaded: (() -> Unit)? = null, colorFilter: androidx.compose.ui.graphics.ColorFilter? = null) {
    if (url == null) return
    val ctx = LocalContext.current
    val motion = Nettle.motion
    val req = remember(url, motion) { ImageRequest.Builder(ctx).data(url).crossfade(if (motion) 150 else 0).build() }
    AsyncImage(req, null, AppServices.images, modifier, contentScale = contentScale, alignment = alignment, colorFilter = colorFilter,
        onSuccess = if (onLoaded == null) null else { _ -> onLoaded() })
}

/**
 * Title logos are nearly always white, made for dark artwork: on a light theme's pale fade they vanish.
 * There, draw the logo's shape in the theme's text colour instead.
 */
@Composable
fun logoTint(): androidx.compose.ui.graphics.ColorFilter? {
    val p = Nettle.colors
    return if (p.isLight) androidx.compose.ui.graphics.ColorFilter.tint(p.text, androidx.compose.ui.graphics.BlendMode.SrcIn) else null
}

/** ThinProgress (4 tall, radius 2, #40FFFFFF track, accent fill) painted directly, for flattened cards. */
fun androidx.compose.ui.graphics.drawscope.DrawScope.drawThinProgress(value: Float, accent: Color, x: Float, y: Float, width: Float) {
    val h = 4.dp.toPx(); val r = CornerRadius(2.dp.toPx())
    drawRoundRect(Color(0x40FFFFFF), Offset(x, y), Size(width, h), r)
    val fw = width * value.coerceIn(0f, 1f)
    if (fw <= 0f) return
    // The fill keeps a square right end, as when it was clipped by the rounded track.
    clipPath(androidx.compose.ui.graphics.Path().apply { addRoundRect(androidx.compose.ui.geometry.RoundRect(x, y, x + width, y + h, r)) }) {
        drawRect(accent, Offset(x, y), Size(fw, h))
    }
}

/** Nettle's logo: the sea nettle on a tile with the theme's accent gradient, plus the wordmark. */
@Composable
fun BrandMark(scale: Float = 1f, showWordmark: Boolean = true, modifier: Modifier = Modifier) {
    val p = Nettle.colors
    Row(modifier, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy((12 * scale).dp)) {
        NettleTile((40 * scale).dp, (12 * scale).dp, (5.5f * scale).dp)
        if (showWordmark) T("Nettle", size = (26 * scale).sp, weight = FontWeight.ExtraBold, style = Nettle.type.body.copy(letterSpacing = (-0.5).sp), color = p.text)
    }
}

@Composable
fun NettleTile(size: Dp, radius: Dp, inset: Dp) {
    val p = Nettle.colors
    Box(Modifier.size(size).clip(RoundedCornerShape(radius)).background(p.accentGradient), contentAlignment = Alignment.Center) {
        Image(painterResource(R.drawable.nettle_glyph), null, Modifier.fillMaxSize().padding(inset), colorFilter = ColorFilter.tint(p.onAccent))
    }
}

/** Keeps the focused item clear of the edges when a list scrolls (the desktop's SpatialNavigation margin). */
@OptIn(ExperimentalFoundationApi::class)
class EdgeSpec(private val before: Float, private val after: Float) : BringIntoViewSpec {
    override fun calculateScrollDistance(offset: Float, size: Float, containerSize: Float): Float {
        val end = offset + size
        return when {
            offset < before -> offset - before
            end > containerSize - after -> minOf(end - (containerSize - after), offset - before)
            else -> 0f
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun KeepClear(before: Dp, after: Dp = before, content: @Composable () -> Unit) {
    val d = androidx.compose.ui.platform.LocalDensity.current
    val spec = remember(before, after, d) { with(d) { EdgeSpec(before.toPx(), after.toPx()) } }
    CompositionLocalProvider(LocalBringIntoViewSpec provides spec, content = content)
}

/** Remembers which control on a page had focus, so coming back to the page puts focus there again. */
fun Modifier.rememberFocus(page: Page?, key: String): Modifier =
    if (page == null) this else this.focusRequester(page.requester(key)).onFocusChanged { if (it.isFocused) page.lastFocusKey = key }

/** Media key handling on a focused card: Play starts it straight away. */
private fun Modifier.playKey(onPlay: (() -> Unit)?): Modifier = if (onPlay == null) this else onPreviewKeyEvent {
    if (it.type == KeyEventType.KeyDown && (it.nativeKeyEvent.keyCode == AndroidKey.KEYCODE_MEDIA_PLAY || it.nativeKeyEvent.keyCode == AndroidKey.KEYCODE_MEDIA_PLAY_PAUSE)) {
        onPlay(); true
    } else false
}

/**
 * Media card (Button.card): no chrome; the frame turns Accent and the card lifts 4.5% when
 * focused, the play hint fades in; watched tick or unwatched count, progress bar.
 */
@Composable
fun MediaCardView(card: MediaCard, modifier: Modifier = Modifier, page: Page? = null) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val motion = Nettle.motion
    val scale by animateFloatAsState(if (focused && motion) 1.045f else 1f, tween(160, easing = CubicOut), label = "lift")
    // The play hint's fade is read in the draw phase only.
    val hint = animateFloatAsState(if (focused) 1f else 0f, tween(if (motion) 150 else 0), label = "hint")
    Column(
        modifier
            .width(card.cardWidth.dp)
            .rememberFocus(page, "card:" + card.item.id)
            .playKey(if (card.item.isPlayable) card::play else null)
            .graphicsLayer { scaleX = scale; scaleY = scale }
            .clickable(source, indication = null) { card.open() },
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        // Flattened for the TV's GPU: one clipped layer for the image; the surface, progress bar,
        // watched tick, play hint and focus frame are painted in a single draw pass around it
        // (same look as the separate boxes they replace, a fraction of the draw commands).
        val shape = RoundedCornerShape(12.dp)
        var loaded by remember(card.imageUrl) { mutableStateOf(false) }
        val play = rememberVectorPainter(Icons.Play)
        val check = rememberVectorPainter(Icons.Check)
        val focusedNow by rememberUpdatedState(focused)
        Box(
            Modifier.fillMaxWidth().height(card.imageHeight.dp)
                .drawWithContent {
                    val r = CornerRadius(12.dp.toPx())
                    drawRoundRect(p.surface, cornerRadius = r)
                    drawContent()
                    if (card.hasProgress) drawThinProgress(card.progress, p.accent, 10.dp.toPx(), size.height - 14.dp.toPx(), size.width - 20.dp.toPx())
                    if (card.isPlayed) {
                        val d = 26.dp.toPx(); val c = Offset(size.width - 8.dp.toPx() - d / 2, 8.dp.toPx() + d / 2)
                        drawCircle(p.accent, d / 2, c)
                        val s = 14.dp.toPx()
                        translate(c.x - s / 2, c.y - s / 2) { with(check) { draw(Size(s, s), colorFilter = ColorFilter.tint(p.onAccent)) } }
                    }
                    val a = hint.value
                    if (card.item.isPlayable && a > 0f) {
                        drawRect(Color(0x55000000), alpha = a)
                        val c = center
                        val icon = 26.dp.toPx(); val rad = icon / 2 + 14.dp.toPx()
                        drawCircle(Color(0x33FFFFFF), rad, c, alpha = a)
                        translate(c.x - icon / 2, c.y - icon / 2) { with(play) { draw(Size(icon, icon), alpha = a, colorFilter = ColorFilter.tint(Color.White)) } }
                    }
                    if (focusedNow) {
                        val w = 2.5.dp.toPx()
                        drawRoundRect(p.accent, topLeft = Offset(w / 2, w / 2), size = Size(size.width - w, size.height - w),
                            cornerRadius = CornerRadius(r.x - w / 2), style = Stroke(w))
                    }
                },
        ) {
            // Title as a placeholder only until the picture arrives.
            if (!loaded) T(card.title, Modifier.align(Alignment.Center).padding(12.dp), muted = true, align = androidx.compose.ui.text.style.TextAlign.Center)
            NetImage(card.imageUrl, Modifier.fillMaxSize().clip(shape), onLoaded = { loaded = true })
            card.badge?.let {
                Box(Modifier.align(Alignment.TopEnd).padding(8.dp).defaultMinSize(minWidth = 26.dp).height(26.dp).background(p.accent, CircleShape).padding(horizontal = 7.dp),
                    contentAlignment = Alignment.Center) { T(it, size = 12.sp, weight = FontWeight.Bold, color = p.onAccent) }
            }
        }
        Column {
            T(card.title, Modifier.padding(horizontal = 2.dp), weight = FontWeight.SemiBold, maxLines = 1, lineHeight = 19.sp)
            if (!card.subtitle.isNullOrEmpty())
                T(card.subtitle, Modifier.padding(start = 2.dp, end = 2.dp, top = 2.dp, bottom = 2.dp), style = Nettle.type.caption, muted = true, maxLines = 2, lineHeight = 17.sp)
        }
    }
}

/** Horizontal row of cards with an h3 title (CardRowViewModel's template). */
@Composable
fun CardRowView(row: CardRow, state: LazyListState = remember { io.github.devall96.nettle.tv.vm.tvListState() }, page: Page? = null, startPadding: Dp = 56.dp) {
    Column(Modifier.padding(bottom = 22.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        T(row.title, Modifier.padding(start = startPadding), style = Nettle.type.h3)
        KeepClear(before = startPadding + 24.dp, after = 80.dp) {
            LazyRow(state = state, contentPadding = PaddingValues(start = startPadding, top = 10.dp, end = 56.dp, bottom = 26.dp),
                horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                items(row.items, key = { it.item.id }) { MediaCardView(it, page = page) }
            }
        }
    }
}

/**
 * A TV text box: D-pad focus only highlights it; OK opens the on-screen keyboard (focus alone
 * must not). Done / Back closes the keyboard and returns focus to the box. Looks like the
 * desktop TextBox: radius 12, Surface, Accent border when focused.
 */
@Composable
fun TvTextField(
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "",
    password: Boolean = false,
    fontSize: Float = 15f,
    padding: PaddingValues = PaddingValues(14.dp, 11.dp),
    leading: (@Composable () -> Unit)? = null,
    keyboardType: KeyboardType = KeyboardType.Text,
    focusRequester: FocusRequester = remember { FocusRequester() },
    onSubmit: () -> Unit = {},
) {
    val p = Nettle.colors
    var editing by remember { mutableStateOf(false) }
    val field = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    var fieldFocused by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(12.dp)
    val active = focused || fieldFocused
    val style = Nettle.type.body.copy(fontSize = fontSize.sp, color = p.text)

    LaunchedEffect(editing) {
        if (editing) { field.requestFocus(); keyboard?.show() }
    }
    fun stop(submit: Boolean) {
        editing = false
        keyboard?.hide()
        focusRequester.requestFocus()
        if (submit) onSubmit()
    }

    Box(
        modifier
            .clip(shape)
            .background(if (active) p.surfaceHigh else p.surface)
            .border(1.dp, if (active) p.accent else p.border, shape)
            .then(if (active) Modifier.border(2.dp, p.accent, shape) else Modifier)
            .focusRequester(focusRequester)
            .clickable(source, indication = null, enabled = !editing) { editing = true }
            .padding(padding),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (leading != null) { leading(); Spacer(Modifier.width(10.dp)) }
            Box(Modifier.weight(1f)) {
                if (editing) {
                    BasicTextField(
                        value, onValueChange,
                        Modifier.fillMaxWidth().focusRequester(field)
                            .onFocusChanged {
                                val was = fieldFocused
                                fieldFocused = it.isFocused
                                // D-pad up / down left the box while typing: stop editing.
                                if (was && !it.isFocused) { editing = false; keyboard?.hide() }
                            }
                            .onPreviewKeyEvent {
                                if (it.type == KeyEventType.KeyUp && it.nativeKeyEvent.keyCode == AndroidKey.KEYCODE_BACK) { stop(false); true } else false
                            },
                        textStyle = style, singleLine = true, cursorBrush = SolidColor(p.accent),
                        visualTransformation = if (password) PasswordVisualTransformation('•') else VisualTransformation.None,
                        keyboardOptions = KeyboardOptions(keyboardType = if (password) KeyboardType.Password else keyboardType, imeAction = ImeAction.Done, autoCorrectEnabled = false),
                        keyboardActions = KeyboardActions(onDone = { stop(true) }),
                    )
                } else {
                    val shown = if (password) "•".repeat(value.length) else value
                    if (shown.isEmpty()) T(placeholder, style = style, muted = true, maxLines = 1)
                    else T(shown, style = style, maxLines = 1)
                }
            }
        }
    }
}

/** A settings-style row: the whole row is focusable and OK flips the switch. */
@Composable
fun ToggleRow(title: String, checked: Boolean, onChange: (Boolean) -> Unit, modifier: Modifier = Modifier, hint: String? = null) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val shape = RoundedCornerShape(12.dp)
    Row(
        modifier.fillMaxWidth().clip(shape).border(2.dp, if (focused) p.accent else Color.Transparent, shape)
            .clickable(source, indication = null) { onChange(!checked) }.padding(horizontal = 10.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            T(title, weight = FontWeight.SemiBold)
            if (hint != null) T(hint, style = Nettle.type.caption, muted = true)
        }
        Spacer(Modifier.width(16.dp))
        ToggleKnob(checked)
    }
}

/** CheckBox with a label (Fluent look), focusable as a whole. */
@Composable
fun CheckRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true,
             detail: String? = null) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val shape = RoundedCornerShape(8.dp)
    Row(
        modifier.clip(shape).border(2.dp, if (focused) p.accent else Color.Transparent, shape)
            .clickable(source, indication = null, enabled = enabled) { onChange(!checked) }.padding(6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        CheckMark(checked, enabled)
        Spacer(Modifier.width(12.dp))
        Column {
            T(label, color = if (enabled) p.text else p.textMuted, weight = if (detail != null) FontWeight.SemiBold else null)
            if (!detail.isNullOrEmpty()) T(detail, style = Nettle.type.caption, muted = true)
        }
    }
}
