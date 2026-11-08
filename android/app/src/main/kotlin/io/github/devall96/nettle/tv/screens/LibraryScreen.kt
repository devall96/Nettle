package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.background
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.LibraryModel
import io.github.devall96.nettle.tv.vm.Main
import kotlin.math.floor

/** LibraryView: title + count, Play all / Shuffle, filters and sort, the grid, the A–Z strip. */
@Composable
fun LibraryScreen(vm: LibraryModel) {
    val p = Nettle.colors
    // Columns as on desktop: (usable + 18) / (card + 18), usable = page width − 56 − 40 − 12.
    val pageWidth = Main.canvasWidth - 80
    LaunchedEffect(pageWidth, vm.cardWidth) {
        vm.setColumnCount(floor((pageWidth - 108 + 18).toDouble() / (vm.cardWidth + 18)).toInt())
    }
    // Infinite scroll: load more when near the end.
    val nearEnd by remember { derivedStateOf { vm.listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index?.let { it >= vm.rows.size - 3 } ?: false } }
    LaunchedEffect(nearEnd) { if (nearEnd) vm.loadMore() }
    LaunchedEffect(vm.scrollToRow) {
        vm.scrollToRow?.let { vm.listState.scrollToItem(it); vm.scrollToRow = null }
    }
    var focusedOnce by remember { mutableStateOf(false) }
    LaunchedEffect(vm.rows.size > 0) {
        if (focusedOnce || vm.rows.isEmpty()) return@LaunchedEffect
        kotlinx.coroutines.delay(50)
        val target = vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: vm.requester("card:" + vm.rows[0][0].item.id)
        focusedOnce = runCatching { target.requestFocus() }.isSuccess
    }

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(start = 56.dp, top = 40.dp, end = 56.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                T(vm.title, style = Nettle.type.h1, maxLines = 1)
                T(vm.countText, muted = true)
            }
            Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    NButton({ vm.playAll() }, Modifier.rememberFocus(vm, "playall"), ButtonKind.Primary) { fg ->
                        Icon(Icons.Play, size = 16.dp, tint = fg); Spacer(Modifier.width(8.dp)); BT("Play all")
                    }
                    NButton({ vm.shuffle() }, Modifier.rememberFocus(vm, "shuffle")) { fg ->
                        Icon(Icons.Shuffle, size = 16.dp, tint = fg); Spacer(Modifier.width(8.dp)); BT("Shuffle")
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    if (vm.showExtraFilters) {
                        Dropdown(vm.genreOptions, vm.genre, { vm.changeGenre(it) }, minWidth = 140.dp, label = { it.label })
                        Dropdown(LibraryModel.decadeOptions, vm.decade, { vm.changeDecade(it) }, minWidth = 120.dp, label = { it.label })
                        Dropdown(LibraryModel.qualityOptions, vm.quality, { vm.changeQuality(it) }, minWidth = 120.dp, label = { it.label })
                    }
                    Dropdown(LibraryModel.filterOptions, vm.filter, { vm.changeFilter(it) }, minWidth = 140.dp, label = { it.label })
                    Dropdown(LibraryModel.sortOptions, vm.sort, { vm.changeSort(it) }, minWidth = 160.dp, label = { it.label })
                }
            }
        }
        Box(Modifier.weight(1f).fillMaxWidth()) {
            KeepClear(before = 40.dp, after = 60.dp) {
                LazyColumn(Modifier.fillMaxSize(), state = vm.listState, contentPadding = PaddingValues(start = 56.dp, top = 8.dp, end = 40.dp, bottom = 40.dp)) {
                    itemsIndexed(vm.rows, key = { _, r -> r.first().item.id }) { _, row ->
                        Row(Modifier.padding(bottom = 22.dp), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                            row.forEach { MediaCardView(it, page = vm) }
                        }
                    }
                }
            }
            // A–Z jump (when sorted by name)
            if (vm.showLetters && vm.rows.isNotEmpty()) Column(Modifier.align(Alignment.CenterEnd).padding(end = 10.dp)) {
                LibraryModel.letters.forEach { l -> Letter(l) { vm.jumpTo(l) } }
            }
            if (vm.isBusy) Spinner(Modifier.align(Alignment.Center))
            ErrorText(vm.error, Modifier.padding(horizontal = 56.dp))
        }
    }
}

@Composable
private fun Letter(l: String, onClick: () -> Unit) {
    val p = Nettle.colors
    val s = remember { MutableInteractionSource() }
    val f by s.collectIsFocusedAsState()
    Box(
        Modifier.widthIn(min = 24.dp).clip(RoundedCornerShape(6.dp)).background(if (f) p.accent else androidx.compose.ui.graphics.Color.Transparent)
            .clickable(s, indication = null, onClick = onClick).padding(horizontal = 6.dp, vertical = 1.dp),
        contentAlignment = Alignment.Center,
    ) { T(l, Modifier.alpha(if (f) 1f else 0.7f), size = 11.sp, weight = FontWeight.SemiBold, color = if (f) p.onAccent else p.text) }
}
