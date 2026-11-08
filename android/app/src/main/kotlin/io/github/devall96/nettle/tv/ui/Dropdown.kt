package io.github.devall96.nettle.tv.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.DpOffset
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle

/**
 * ComboBox (Fluent look, radius 10): shows the selected option with a chevron; OK opens the
 * list, D-pad picks, OK chooses, Back closes.
 */
@Composable
fun <T> Dropdown(
    options: List<T>,
    selected: T?,
    onSelect: (T) -> Unit,
    modifier: Modifier = Modifier,
    minWidth: Dp = 180.dp,
    label: (T) -> String = { it.toString() },
) {
    val p = Nettle.colors
    var open by remember { mutableStateOf(false) }
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val self = remember { FocusRequester() }
    val shape = RoundedCornerShape(10.dp)
    Box(modifier) {
        Row(
            Modifier.widthIn(min = minWidth).clip(shape).background(if (p.isLight) Color(0xB3FFFFFF) else Color(0x0FFFFFFF))
                .border(if (focused) 2.dp else 1.dp, if (focused) p.accent else p.textMuted.copy(alpha = 0.7f), shape)
                .focusRequester(self)
                .clickable(source, indication = null) { open = true }
                .padding(start = 12.dp, end = 10.dp, top = 7.dp, bottom = 7.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            T(selected?.let(label) ?: "", Modifier.widthIn(max = 260.dp), maxLines = 1)
            Spacer(Modifier.width(10.dp))
            Icon(Icons.ChevronRight, Modifier.rotate(90f), size = 14.dp, tint = p.textMuted)
        }
        if (open) {
            val first = remember { FocusRequester() }
            val start = options.indexOf(selected).coerceAtLeast(0)
            val listState = rememberLazyListState(initialFirstVisibleItemIndex = maxOf(0, start - 3))
            // A popup is its own window and gets the TV's density back: keep Nettle's 1440-unit canvas inside it.
            val canvasDensity = androidx.compose.ui.platform.LocalDensity.current
            Popup(
                offset = IntOffset(0, 0),
                onDismissRequest = { open = false; runCatching { self.requestFocus() } },
                properties = PopupProperties(focusable = true),
            ) { CompositionLocalProvider(androidx.compose.ui.platform.LocalDensity provides canvasDensity) {
                LaunchedEffect(Unit) { runCatching { first.requestFocus() } }
                Box(Modifier.widthIn(min = minWidth, max = 420.dp).heightIn(max = 420.dp).clip(RoundedCornerShape(10.dp)).background(p.surfaceHigh)
                    .border(1.dp, p.border, RoundedCornerShape(10.dp)).padding(4.dp)) {
                    LazyColumn(state = listState) {
                        itemsIndexed(options) { i, o ->
                            val s = remember { MutableInteractionSource() }
                            val f by s.collectIsFocusedAsState()
                            val isSel = o == selected
                            Box(
                                Modifier.fillMaxWidth().clip(RoundedCornerShape(6.dp))
                                    .background(if (f) p.accent else if (isSel) p.accentSoft else Color.Transparent)
                                    .then(if (i == start) Modifier.focusRequester(first) else Modifier)
                                    .clickable(s, indication = null) { open = false; onSelect(o); runCatching { self.requestFocus() } }
                                    .padding(horizontal = 12.dp, vertical = 8.dp),
                            ) { T(label(o), color = if (f) p.onAccent else p.text, maxLines = 1) }
                        }
                    }
                }
            } }
        }
    }
}
