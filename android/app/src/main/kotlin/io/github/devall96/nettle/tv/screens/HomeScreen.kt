package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import kotlinx.coroutines.launch
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.*
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.HeroItem
import io.github.devall96.nettle.tv.vm.HomeModel

/** HomeView: 560-tall spotlight, then the rows. */
@Composable
fun HomeScreen(vm: HomeModel) {
    val p = Nettle.colors
    val play = vm.requester("hero:play")
    var focusedOnce by remember { mutableStateOf(false) }
    // Tells Android (and start-up measurements) when Home is usable: rows painted.
    androidx.activity.compose.ReportDrawnWhen { vm.rows.isNotEmpty() }
    // First visit: focus the banner's Play button (or the first card). Coming back: where focus was.
    LaunchedEffect(vm.hero != null, vm.rows.size) {
        if (focusedOnce) return@LaunchedEffect
        val target = vm.lastFocusKey?.let { vm.focusRequesters[it] }
            ?: if (vm.hero != null) play else vm.rows.firstOrNull()?.items?.firstOrNull()?.let { vm.requester("card:" + it.item.id) }
        if (target != null) { kotlinx.coroutines.delay(50); if (runCatching { target.requestFocus() }.isSuccess) focusedOnce = true }
    }
    Box(Modifier.fillMaxSize()) {
        KeepClear(before = 90.dp, after = 40.dp) {
            LazyColumn(Modifier.fillMaxSize(), state = vm.listState) {
                val hero = vm.hero
                if (hero != null) item(key = "hero") { Hero(vm, hero, play) }
                else item(key = "title") {
                    Column {
                        Spacer(Modifier.height(48.dp))
                        T("Home", Modifier.padding(start = 56.dp, bottom = 24.dp), style = Nettle.type.h1)
                    }
                }
                vm.error?.let { e -> item(key = "error") { ErrorText(e, Modifier.padding(horizontal = 56.dp)) } }
                item(key = "top") { Spacer(Modifier.height(0.dp)) }
                items(vm.rows, key = { it.title }) { row ->
                    Box(Modifier.offset(y = (-8).dp)) {
                        CardRowView(row, vm.rowStates.getOrPut(row.title) { io.github.devall96.nettle.tv.vm.tvListState() }, vm)
                    }
                }
                item(key = "bottom") { Spacer(Modifier.height(40.dp)) }
            }
        }
        if (vm.isBusy) Spinner(Modifier.align(Alignment.Center))
    }
}

@Composable
private fun Hero(vm: HomeModel, hero: HeroItem, play: FocusRequester) {
    val p = Nettle.colors
    Box(Modifier.fillMaxWidth().height(560.dp)) {
        NetImage(hero.backdropUrl, Modifier.fillMaxSize(), alignment = Alignment.TopCenter)
        // Both fades in one cached, pre-rendered layer (see cachedGradients).
        val fades = remember(p) { Modifier.fillMaxSize().cachedGradients(p.heroFadeLeft, p.heroFadeBottom) }
        Box(fades)

        Column(
            Modifier.align(Alignment.BottomStart).padding(start = 56.dp, end = 56.dp, bottom = 56.dp).widthIn(max = 640.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            if (hero.hasLogo) NetImage(hero.logoUrl, Modifier.heightIn(max = 130.dp).widthIn(max = 460.dp), contentScale = ContentScale.Fit, alignment = Alignment.CenterStart, colorFilter = logoTint())
            else T(hero.title, style = Nettle.type.display)
            T(hero.meta, muted = true, weight = FontWeight.SemiBold)
            // 90% text colour instead of a 90% alpha layer: same look without an offscreen buffer.
            hero.overview?.let { T(it, color = p.text.copy(alpha = p.text.alpha * 0.9f), size = 15.sp, lineHeight = 23.sp, maxLines = 3) }
            val scope = rememberCoroutineScope()
            // Coming back up to the banner shows all of it, not just the buttons peeking in at the top.
            Row(Modifier.padding(top = 8.dp).onFocusChanged { if (it.hasFocus && vm.listState.firstVisibleItemIndex == 0 && vm.listState.firstVisibleItemScrollOffset > 0) scope.launch { vm.listState.animateScrollToItem(0) } },
                horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                PlayButton("Play", { hero.play() }, Modifier.rememberFocus(vm, "hero:play"))
                NButton({ hero.info() }, Modifier.rememberFocus(vm, "hero:info").onPreviewKeyEvent {
                    // Right from "More info" shows the next banner item (there is nothing else to the right).
                    if (it.type == KeyEventType.KeyDown && it.key == Key.DirectionRight) { vm.nextHero(); vm.touchHero(); true } else false
                }, kind = ButtonKind.Glass, padding = PaddingValues(22.dp, 12.dp)) { BT("More info") }
            }
        }
        Row(Modifier.align(Alignment.BottomEnd).padding(end = 56.dp, bottom = 60.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(Icons.ChevronLeft, Icons.ChevronRight).forEach {
                val light = Nettle.colors.isLight
                Box(Modifier.clip(CircleShape).background(if (light) Color(0x26000000) else Color(0x33FFFFFF)).padding(10.dp)) { Icon(it, tint = if (light) Nettle.colors.text else Color.White) }
            }
        }
    }
}
