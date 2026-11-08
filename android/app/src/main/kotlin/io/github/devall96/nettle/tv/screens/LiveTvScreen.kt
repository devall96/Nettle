package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.layout.layout
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.GuideCell
import io.github.devall96.nettle.tv.vm.GuideRow
import io.github.devall96.nettle.tv.vm.LiveTvModel

/** LiveTvView: info panel, time header, channel column + schedule, "now" line. */
@Composable
fun LiveTvScreen(vm: LiveTvModel) {
    val p = Nettle.colors
    if (vm.notConfigured) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            val setup = remember { androidx.compose.ui.focus.FocusRequester() }
            LaunchedEffect(Unit) { runCatching { setup.requestFocus() } }
            Column(Modifier.widthIn(max = 520.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Icon(Icons.LiveTv, size = 64.dp, tint = p.accent)
                T("Live TV with NostalgiaTV", style = Nettle.type.h2)
                T("Point Nettle at your NostalgiaTV server to get its channels and full program guide. Streams play directly in Nettle's player.",
                    muted = true, align = TextAlign.Center)
                TextButton("Set up in Settings", { vm.openSettings() }, kind = ButtonKind.Primary, focusRequester = setup)
            }
        }
        return
    }
    var viewWidthPx by remember { mutableStateOf(0) }
    val d = LocalDensity.current
    val viewWidth = with(d) { viewWidthPx.toDp().value }
    var focusedOnce by remember { mutableStateOf(false) }

    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            // Info panel for the highlighted program
            Row(Modifier.fillMaxWidth().height(200.dp).padding(start = 56.dp, top = 40.dp, end = 56.dp).padding(bottom = 0.dp)) {
                Box(Modifier.padding(end = 28.dp).size(300.dp, 170.dp).clip(RoundedCornerShape(14.dp)).background(p.surface).padding(10.dp)) {
                    NetImage(vm.infoImage, Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    vm.infoChannel?.let { SectionLabel(it) }
                    T(vm.infoTitle ?: "", style = Nettle.type.h1, maxLines = 1)
                    vm.infoSub?.takeIf { it.isNotEmpty() }?.let { T(it, muted = true, maxLines = 1) }
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                        vm.infoTime?.let { T(it, weight = FontWeight.SemiBold) }
                        if (vm.infoProgress > 0f) ThinProgress(vm.infoProgress, Modifier.width(140.dp))
                    }
                    vm.infoDesc?.let { T(it, Modifier.widthIn(max = 760.dp), muted = true, maxLines = 3) }
                }
                IconButton(Icons.Refresh, { vm.reload() })
            }
            Spacer(Modifier.height(18.dp))
            // Time header
            Row(Modifier.fillMaxWidth().padding(start = 56.dp, end = 56.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Row(Modifier.width(LiveTvModel.CHANNEL_COLUMN.dp), horizontalArrangement = Arrangement.spacedBy(4.dp), verticalAlignment = Alignment.CenterVertically) {
                    IconButton(Icons.ChevronLeft, { vm.earlier(viewWidth) })
                    TextButton(vm.dayLabel, { vm.jumpToNow(viewWidth) }, kind = ButtonKind.Chip)
                    IconButton(Icons.ChevronRight, { vm.later(viewWidth) })
                }
                Box(Modifier.weight(1f).height(24.dp).clipToBounds().onSizeChanged { viewWidthPx = it.width }) {
                    // Scrolling only moves the labels (layout phase); they are recomposed only when a new half hour comes into view.
                    val first by remember { derivedStateOf { (vm.scrollX / SLOT).toInt().coerceAtLeast(0) } }
                    val count = (viewWidth / SLOT).toInt() + 2
                    for (i in first until minOf(first + count, vm.totalMinutes / 30)) {
                        key(i) {
                            T(vm.slotLabel(i * 30), Modifier.offset { IntOffset((i * SLOT - vm.scrollX).dp.roundToPx(), 0) }.width(SLOT.dp).padding(horizontal = 4.dp),
                                muted = true, weight = FontWeight.SemiBold, maxLines = 1)
                        }
                    }
                }
            }
            // Guide
            Box(Modifier.weight(1f).fillMaxWidth().padding(start = 56.dp, end = 56.dp, bottom = 24.dp)
                // "Now" line, drawn over the rows: scrolling only redraws it.
                .drawWithContent {
                    drawContent()
                    val nowX = vm.xOf(vm.nowMs) - vm.scrollX
                    if (nowX >= 0 && nowX <= viewWidth) drawRect(p.accent.copy(alpha = 0.85f),
                        topLeft = Offset((LiveTvModel.CHANNEL_COLUMN + nowX).dp.toPx(), 0f), size = Size(2.dp.toPx(), size.height))
                },
            ) {
                KeepClear(before = 20.dp, after = 20.dp) {
                    LazyColumn(Modifier.fillMaxSize(), state = vm.listState) {
                        items(vm.rows, key = { it.index }) { row ->
                            GuideRowView(vm, row, viewWidth, onFirstFocus = { focusedOnce = true }, requestFirst = !focusedOnce && row.index == 0)
                        }
                    }
                }
            }
        }
        if (vm.isBusy) Spinner(Modifier.align(Alignment.Center))
        ErrorText(vm.error, Modifier.padding(start = 56.dp, top = 260.dp))
    }
}

@Composable
private fun GuideRowView(vm: LiveTvModel, row: GuideRow, viewWidth: Float, onFirstFocus: () -> Unit, requestFirst: Boolean) {
    val p = Nettle.colors
    Row(Modifier.fillMaxWidth().height(68.dp + 4.dp).padding(bottom = 4.dp)) {
        // Channel cell
        Row(
            Modifier.width((LiveTvModel.CHANNEL_COLUMN - 6).dp).fillMaxHeight().clip(RoundedCornerShape(10.dp)).background(p.surface).padding(horizontal = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.size(60.dp, 44.dp).clip(RoundedCornerShape(6.dp)), contentAlignment = Alignment.Center) {
                T(row.initials, weight = FontWeight.Bold, muted = true)
                NetImage(row.logoUrl, Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
            }
            Column(Modifier.padding(start = 12.dp)) {
                row.number?.let { T(it, style = Nettle.type.caption, color = p.accent, weight = FontWeight.Bold) }
                T(row.name, weight = FontWeight.SemiBold, maxLines = 1)
            }
        }
        Spacer(Modifier.width(6.dp))
        // Programs around the visible time
        Box(Modifier.weight(1f).fillMaxHeight().clipToBounds()) {
            // The composed window moves in 10-minute steps, so a D-pad move doesn't recompose the row;
            // the cells themselves follow the scroll in the layout phase.
            val window by remember(viewWidth) {
                derivedStateOf {
                    val from = vm.msAt(vm.scrollX - 120 * LiveTvModel.PX_PER_MINUTE) / 600_000
                    val to = vm.msAt(vm.scrollX + viewWidth + 240 * LiveTvModel.PX_PER_MINUTE) / 600_000
                    from to to
                }
            }
            val cells = remember(row, window) { row.cellsIn(window.first * 600_000, (window.second + 1) * 600_000) }
            cells.forEach { c ->
                key(c.key) {
                    ProgramCell(vm, c, viewWidth, requestFirst && c.startMs <= vm.nowMs && c.endMs > vm.nowMs, onFirstFocus)
                }
            }
        }
    }
}

@Composable
private fun ProgramCell(vm: LiveTvModel, c: GuideCell, viewWidth: Float, requestFocus: Boolean, onFocused: () -> Unit) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val isNow = c.program != null && c.startMs <= vm.nowMs && c.endMs > vm.nowMs
    val x0 = vm.xOf(c.startMs)
    val w = (vm.xOf(c.endMs) - x0).coerceAtLeast(1f)
    val shape = RoundedCornerShape(10.dp)
    val fr = vm.requester("cell:" + c.key)
    LaunchedEffect(requestFocus) { if (requestFocus) { kotlinx.coroutines.delay(60); if (runCatching { fr.requestFocus() }.isSuccess) onFocused() } }
    Box(
        // Read the scroll only while placing (no recomposition when the guide scrolls).
        Modifier.offset { IntOffset((x0 - vm.scrollX).dp.roundToPx(), 0) }.width(w.dp).fillMaxHeight()
            .padding(end = 4.dp) // the 4-unit right border in BgBrush separates cells
            .clip(shape)
            .background(when { focused -> p.accent; c.isGap -> Color.Transparent; isNow -> p.surfaceHigh; else -> p.surface })
            .then(if (c.isGap && !focused) Modifier.drawBehind {
                drawRoundRect(p.border, style = androidx.compose.ui.graphics.drawscope.Stroke(1.dp.toPx()),
                    cornerRadius = androidx.compose.ui.geometry.CornerRadius(10.dp.toPx()))
            } else Modifier)
            .rememberFocus(vm, "cell:" + c.key)
            .onFocusChanged { if (it.isFocused) vm.onCellFocused(c, viewWidth) }
            .clickable(source, indication = null) { vm.playChannel(c.row) }
            .padding(horizontal = 12.dp, vertical = 6.dp),
        contentAlignment = Alignment.CenterStart,
    ) {
        // Keep the title readable when the program started before the left edge.
        // Measured in the layout phase from the current scroll; never negative (short cells used to crash here).
        Column(Modifier.layout { m, cs ->
            val x = x0 - vm.scrollX
            val inset = if (x < 0) (-x).coerceAtMost(w - 40f).coerceAtLeast(0f).dp.roundToPx().coerceAtMost(cs.maxWidth) else 0
            val pl = m.measure(cs.copy(minWidth = 0, maxWidth = (cs.maxWidth - inset).coerceAtLeast(0)))
            layout(pl.width + inset, pl.height) { pl.place(inset, 0) }
        }) {
            val fg = if (focused) p.onAccent else p.text
            T(if (c.isGap) "" else c.title, weight = FontWeight.SemiBold, color = fg, maxLines = 1)
            if (w > 110 && !c.isGap) T(c.time, style = Nettle.type.caption, color = if (focused) p.onAccent else p.textMuted, maxLines = 1)
        }
    }
}

/** Width of one half-hour slot in the guide, in Nettle units. */
private const val SLOT = 30 * LiveTvModel.PX_PER_MINUTE
