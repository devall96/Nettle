package io.github.devall96.nettle.tv.screens

import androidx.compose.ui.focus.focusRestorer
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.*

/** RequestsView: search TMDB (through Seerr), My Requests, discover rows, season picker. */
@Composable
fun RequestsScreen(vm: RequestsModel) {
    val p = Nettle.colors
    if (vm.notConfigured) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            val setup = remember { FocusRequester() }
            LaunchedEffect(Unit) { runCatching { setup.requestFocus() } }
            Column(Modifier.widthIn(max = 520.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Icon(Icons.Request, size = 64.dp, tint = p.accent)
                T("Request movies & shows", style = Nettle.type.h2)
                T("Connect your Seerr server to browse what's trending and request anything that isn't in your library yet.", muted = true, align = TextAlign.Center)
                TextButton("Set up in Settings", { vm.openSettings() }, kind = ButtonKind.Primary, focusRequester = setup)
            }
        }
        return
    }
    val box = vm.requester("box")
    LaunchedEffect(Unit) { kotlinx.coroutines.delay(50); runCatching { (vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: box).requestFocus() } }
    Box(Modifier.fillMaxSize()) {
        KeepClear(before = 60.dp, after = 40.dp) {
            LazyColumn(Modifier.fillMaxSize(), state = vm.listState, contentPadding = PaddingValues(top = 40.dp, bottom = 40.dp)) {
                item(key = "head") {
                    Column(Modifier.padding(start = 56.dp, end = 56.dp, bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        T("Requests", style = Nettle.type.h1)
                        Row(Modifier.width(760.dp), verticalAlignment = Alignment.CenterVertically) {
                            TvTextField(vm.searchText, { vm.setSearch(it) }, Modifier.weight(1f).rememberFocus(vm, "box"), placeholder = "Search TMDB for a movie or show to request…",
                                fontSize = 17f, focusRequester = box, leading = { Icon(Icons.Search, size = 18.dp, tint = p.textMuted) })
                            if (vm.isSearching) IconButton(Icons.Close, { vm.clearSearch() }, Modifier.padding(start = 8.dp))
                        }
                    }
                }
                vm.error?.let { e -> item(key = "err") { ErrorText(e, Modifier.padding(start = 56.dp, end = 56.dp, bottom = 12.dp)) } }
                if (vm.isSearching) {
                    // Search results as a wrapping grid
                    val perRow = maxOf(1, ((Main.canvasWidth - 80 - 56 - 40) / (164 + 18)))
                    val chunks = vm.searchResults.chunked(perRow)
                    items(chunks.size, key = { "s$it" }) { i ->
                        Row(Modifier.padding(start = 56.dp, bottom = 22.dp), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                            chunks[i].forEach { SeerrCardView(vm, it) }
                        }
                    }
                } else {
                    if (vm.myRequests.isNotEmpty()) item(key = "mine") {
                        Column(Modifier.padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            T("My Requests", Modifier.padding(start = 56.dp), style = Nettle.type.h3)
                            KeepClear(before = 80.dp, after = 80.dp) {
                                LazyRow(Modifier.focusRestorer(), state = vm.myState, contentPadding = PaddingValues(start = 56.dp, top = 6.dp, end = 56.dp, bottom = 10.dp),
                                    horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                                    items(vm.myRequests, key = { it.key }) { MyRequestView(vm, it) }
                                }
                            }
                        }
                    }
                    items(vm.rows, key = { it.title }) { row ->
                        Column(Modifier.padding(bottom = 22.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            T(row.title, Modifier.padding(start = 56.dp), style = Nettle.type.h3)
                            KeepClear(before = 80.dp, after = 80.dp) {
                                LazyRow(Modifier.focusRestorer(), state = vm.rowStates.getOrPut(row.title) { io.github.devall96.nettle.tv.vm.tvListState() },
                                    contentPadding = PaddingValues(start = 56.dp, top = 10.dp, end = 56.dp, bottom = 26.dp), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                                    items(row.items, key = { it.result.mediaType + it.result.id }) { SeerrCardView(vm, it) }
                                }
                            }
                        }
                    }
                }
            }
        }
        if (vm.isBusy || vm.isLoadingSeasons) Spinner(Modifier.align(Alignment.Center))
        vm.seasonPicker?.let { SeasonPickerView(it) }
    }
}

/** 164 × 246 poster card with status pill and a "Request" hint when focused. */
@Composable
private fun SeerrCardView(vm: RequestsModel, c: SeerrCard) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val motion = Nettle.motion
    val scale by animateFloatAsState(if (focused && motion) 1.045f else 1f, tween(160), label = "lift")
    val hint by animateFloatAsState(if (focused) 1f else 0f, tween(if (motion) 150 else 0), label = "hint")
    Column(
        Modifier.width(164.dp).graphicsLayer { scaleX = scale; scaleY = scale }.rememberFocus(vm, "seerr:" + c.result.mediaType + c.result.id)
            .clickable(source, indication = null) { c.request() },
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        val shape = RoundedCornerShape(12.dp)
        Box(Modifier.fillMaxWidth().height(246.dp).clip(shape).background(p.surface).border(2.5.dp, if (focused) p.accent else Color.Transparent, shape),
            contentAlignment = Alignment.Center) {
            T(c.title, Modifier.padding(12.dp), muted = true, align = TextAlign.Center)
            NetImage(c.posterUrl, Modifier.fillMaxSize())
            if (c.canRequest) Box(Modifier.fillMaxSize().alpha(hint).background(Color(0x88000000)), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Icon(Icons.Request, size = 34.dp, tint = Color.White)
                    T("Request", weight = FontWeight.Bold, color = Color.White)
                }
            }
            if (c.hasStatus) Box(Modifier.align(Alignment.TopStart).padding(8.dp).clip(CircleShape).background(p.accent).padding(10.dp, 3.dp)) {
                T(c.status, size = 11.5.sp, weight = FontWeight.Bold, color = p.onAccent)
            }
            if (c.isRequesting) Box(Modifier.fillMaxSize().background(Color(0x99000000)), contentAlignment = Alignment.Center) { Spinner(size = 32.dp) }
        }
        Column {
            T(c.title, weight = FontWeight.SemiBold, maxLines = 1)
            T(c.subtitle, style = Nettle.type.caption, muted = true, maxLines = 1)
        }
    }
}

@Composable
private fun MyRequestView(vm: RequestsModel, r: MyRequest) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val shape = RoundedCornerShape(12.dp)
    // Not actionable on desktop either; focusable here only so the row can be scrolled with the remote.
    Row(
        Modifier.width(300.dp).clip(shape).background(p.surface).border(2.dp, if (focused) p.accent else Color.Transparent, shape)
            .rememberFocus(vm, "mine:" + r.key).clickable(source, indication = null) {}.padding(8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(56.dp, 84.dp).clip(RoundedCornerShape(8.dp)).background(p.surfaceHigh)) { NetImage(r.posterUrl, Modifier.fillMaxSize()) }
        Column(Modifier.padding(start = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            T(r.title, weight = FontWeight.SemiBold, maxLines = 1)
            T(r.status, style = Nettle.type.caption, color = p.accent, weight = FontWeight.Bold)
            T(r.whenText, style = Nettle.type.caption, muted = true)
        }
    }
}

@Composable
private fun SeasonPickerView(sp: SeasonPicker) {
    val p = Nettle.colors
    val first = remember { FocusRequester() }
    LaunchedEffect(sp) { runCatching { first.requestFocus() } }
    Box(Modifier.fillMaxSize().background(p.scrim), contentAlignment = Alignment.Center) {
        Panel(Modifier.width(460.dp).heightIn(max = 640.dp)) {
            T(sp.title, style = Nettle.type.h2, maxLines = 1)
            Row(Modifier.padding(top = 6.dp, bottom = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                T("Which seasons do you want?", Modifier.weight(1f), muted = true)
                if (sp.hasSelectable) TextButton("Select all", { sp.selectAll() }, kind = ButtonKind.Ghost)
            }
            Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState())) {
                sp.seasons.forEachIndexed { i, s ->
                    CheckRow(s.name, s.isSelected, { s.isSelected = it }, Modifier.padding(vertical = 4.dp)
                        .then(if (i == sp.seasons.indexOfFirst { o -> o.canSelect }) Modifier.focusRequester(first) else Modifier),
                        enabled = s.canSelect, detail = s.detail)
                }
            }
            Row(Modifier.align(Alignment.End).padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                TextButton("Cancel", { sp.cancel() })
                TextButton(sp.requestText, { sp.request() }, kind = ButtonKind.Primary, enabled = sp.selectedCount > 0)
            }
        }
    }
}

