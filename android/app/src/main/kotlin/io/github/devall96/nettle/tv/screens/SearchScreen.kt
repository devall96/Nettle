package io.github.devall96.nettle.tv.screens

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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.SearchModel
import io.github.devall96.nettle.tv.vm.SeerrSuggestion

/** SearchView: h1, the 760-wide search box, result rows by kind, Seerr suggestions. */
@Composable
fun SearchScreen(vm: SearchModel) {
    val p = Nettle.colors
    val box = vm.requester("box")
    LaunchedEffect(Unit) {
        kotlinx.coroutines.delay(50)
        runCatching { (vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: box).requestFocus() }
    }
    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            Column(Modifier.padding(start = 56.dp, top = 40.dp, end = 56.dp, bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                T("Search", style = Nettle.type.h1)
                TvTextField(vm.query, { vm.updateQuery(it) }, Modifier.width(760.dp).rememberFocus(vm, "box"), placeholder = "Movies, shows, episodes…",
                    fontSize = 20f, padding = PaddingValues(20.dp, 14.dp), focusRequester = box,
                    leading = { Icon(Icons.Search, size = 20.dp, tint = p.textMuted) })
            }
            KeepClear(before = 40.dp, after = 40.dp) {
                LazyColumn(Modifier.weight(1f).fillMaxWidth(), state = vm.listState, contentPadding = PaddingValues(bottom = 40.dp)) {
                    items(vm.results, key = { it.title }) { row -> CardRowView(row, vm.rowStates.getOrPut(row.title) { io.github.devall96.nettle.tv.vm.tvListState() }, vm) }
                    if (vm.noResults) item(key = "none") {
                        Column(Modifier.padding(horizontal = 56.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            T("Nothing in your library matches that.", style = Nettle.type.h3)
                            if (vm.seerrAvailable) TextButton("Search Seerr to request it", { vm.requestIt() }, kind = ButtonKind.Primary)
                        }
                    }
                    if (vm.seerrSuggestions.isNotEmpty()) item(key = "seerr") {
                        Column(Modifier.padding(start = 56.dp, top = 18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                            T("Not in your library: request it", style = Nettle.type.h3)
                            KeepClear(before = 24.dp, after = 80.dp) {
                                LazyRow(state = vm.seerrState, contentPadding = PaddingValues(top = 4.dp, bottom = 12.dp, end = 56.dp), horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                                    items(vm.seerrSuggestions, key = { it.r.mediaType + it.r.id }) { SuggestionCard(vm, it) }
                                }
                            }
                        }
                    }
                }
            }
        }
        if (vm.isBusy) Spinner(Modifier.align(Alignment.TopCenter).padding(top = 180.dp), size = 40.dp)
        ErrorText(vm.error, Modifier.padding(start = 56.dp, top = 180.dp))
    }
}

@Composable
private fun SuggestionCard(vm: SearchModel, s: SeerrSuggestion) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused && Nettle.motion) 1.045f else 1f, tween(160), label = "lift")
    Column(
        Modifier.width(150.dp).graphicsLayer { scaleX = scale; scaleY = scale }.rememberFocus(vm, "seerr:" + s.r.id)
            .clickable(source, indication = null) { s.open() },
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(Modifier.size(150.dp, 225.dp).clip(RoundedCornerShape(10.dp)).background(p.surfaceHigh)
            .border(2.5.dp, if (focused) p.accent else Color.Transparent, RoundedCornerShape(10.dp)), contentAlignment = Alignment.Center) {
            T(s.title, Modifier.padding(10.dp), muted = true, align = TextAlign.Center)
            NetImage(s.posterUrl, Modifier.fillMaxSize())
        }
        T(s.title, weight = FontWeight.SemiBold, maxLines = 1)
        T(s.status, style = Nettle.type.caption, muted = true, maxLines = 1)
    }
}
