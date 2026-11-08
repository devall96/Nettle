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
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.*

/** DetailsView: fixed backdrop, poster + info, seasons & episodes, cast, extras, more like this. */
@Composable
fun DetailsScreen(vm: DetailsModel) {
    val p = Nettle.colors
    val playFocus = vm.requester("play")
    var focused by remember { mutableStateOf(false) }
    LaunchedEffect(vm.item != null) {
        if (vm.item == null || focused) return@LaunchedEffect
        kotlinx.coroutines.delay(50)
        val target = vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: playFocus
        focused = runCatching { target.requestFocus() }.isSuccess
    }
    Box(Modifier.fillMaxSize()) {
        // Backdrop stays fixed while content scrolls over it
        Box(Modifier.fillMaxWidth().height(720.dp)) {
            NetImage(vm.backdropUrl, Modifier.fillMaxSize(), alignment = Alignment.TopCenter)
            // Both fades in one cached, pre-rendered layer (see cachedGradients).
            val fades = remember(p) { Modifier.fillMaxSize().cachedGradients(p.heroFadeLeft, p.heroFadeBottom) }
            Box(fades)
        }
        if (vm.item != null) KeepClear(before = 120.dp, after = 48.dp) {
            LazyColumn(Modifier.fillMaxSize(), state = vm.listState, contentPadding = PaddingValues(start = 56.dp, top = 120.dp, end = 56.dp, bottom = 48.dp),
                verticalArrangement = Arrangement.spacedBy(18.dp)) {
                item(key = "head") { Header(vm) }
                if (vm.isEpisode) item(key = "still") {
                    Box(Modifier.size(480.dp, 270.dp).clip(RoundedCornerShape(14.dp)).background(p.surface)) { NetImage(vm.posterUrl, Modifier.fillMaxSize()) }
                }
                if (vm.isSeries) {
                    item(key = "seasons") {
                        KeepClear(before = 24.dp, after = 24.dp) {
                            LazyRow(Modifier.padding(top = 18.dp), state = vm.seasonState, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                items(vm.seasons, key = { it.season.id }) { s ->
                                    TextButton(s.name, { vm.selectSeason(s) }, Modifier.rememberFocus(vm, "season:" + s.season.id), kind = ButtonKind.Chip, selected = vm.selectedSeason === s)
                                }
                            }
                        }
                    }
                    items(vm.episodes, key = { "ep:" + it.item.id }) { EpisodeRowView(vm, it) }
                }
                if (vm.cast.isNotEmpty()) item(key = "cast") {
                    Column(Modifier.padding(top = 18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        T("Cast & Crew", style = Nettle.type.h3)
                        KeepClear(before = 24.dp, after = 80.dp) {
                            LazyRow(state = vm.castState, contentPadding = PaddingValues(top = 4.dp, bottom = 12.dp), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                                items(vm.cast, key = { (it.p.id ?: "") + it.role + it.name }) { PersonView(vm, it) }
                            }
                        }
                    }
                }
                if (vm.extras.isNotEmpty()) item(key = "extras") { CardRowSection("Extras", vm.extras, vm, vm.extrasState) }
                if (vm.similar.isNotEmpty()) item(key = "similar") { CardRowSection("More Like This", vm.similar, vm, vm.similarState) }
                vm.error?.let { e -> item(key = "err") { ErrorText(e) } }
            }
        }
        if (vm.isBusy) Spinner(Modifier.align(Alignment.Center))
    }
}

@Composable
private fun Header(vm: DetailsModel) {
    val p = Nettle.colors
    Row {
        // Poster (not for episodes, which show a wide still)
        if (!vm.isEpisode) Box(
            Modifier.padding(end = 40.dp).size(240.dp, 360.dp).shadow(50.dp, RoundedCornerShape(16.dp), ambientColor = Color(0x80000000), spotColor = Color(0x80000000))
                .clip(RoundedCornerShape(16.dp)).background(p.surface),
        ) { NetImage(vm.posterUrl, Modifier.fillMaxSize()) }
        Column(Modifier.align(Alignment.Bottom).widthIn(max = 900.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            if (vm.hasLogo) NetImage(vm.logoUrl, Modifier.heightIn(max = 140.dp).widthIn(max = 520.dp), contentScale = ContentScale.Fit, alignment = Alignment.CenterStart)
            else T(vm.titleText, style = Nettle.type.display)
            vm.subtitleText?.takeIf { it.isNotEmpty() }?.let { T(it, style = Nettle.type.h3) }
            T(vm.meta, weight = FontWeight.SemiBold, muted = true)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                vm.hdrBadge?.let {
                    Box(Modifier.clip(RoundedCornerShape(6.dp)).background(p.accentGradient).padding(8.dp, 2.dp)) {
                        T(it, size = 12.sp, weight = FontWeight.ExtraBold, color = p.onAccent)
                    }
                }
                vm.mediaInfo?.let { T(it, style = Nettle.type.caption, muted = true) }
            }
            vm.hdrWarning?.let { T(it, Modifier.widthIn(max = 700.dp), color = p.warning, size = 12.5.sp) }
            Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                PlayButton(vm.playLabel, { vm.play() }, Modifier.rememberFocus(vm, "play"))
                if (vm.hasProgress) TextButton("From start", { vm.playFromStart() }, Modifier.rememberFocus(vm, "fromstart"))
                if (vm.isSeries) NButton({ vm.shuffleSeries() }, Modifier.rememberFocus(vm, "shuffle")) { fg ->
                    Icon(Icons.Shuffle, size = 16.dp, tint = fg); Spacer(Modifier.width(8.dp)); BT("Shuffle")
                }
                if (vm.hasTrailer) TextButton("Trailer", { vm.playTrailer() }, Modifier.rememberFocus(vm, "trailer"))
                IconButton(Icons.Check, { vm.togglePlayed() }, Modifier.rememberFocus(vm, "played"), active = vm.isPlayed, background = p.surfaceHigh)
                IconButton(Icons.Heart, { vm.toggleFavorite() }, Modifier.rememberFocus(vm, "fav"), active = vm.isFavorite, background = p.surfaceHigh)
                if (vm.isEpisode) TextButton("Go to series", { vm.goToSeries() }, Modifier.rememberFocus(vm, "series"))
                if (vm.hasVersions) Dropdown(vm.versions, vm.selectedVersion, { vm.selectVersion(it) }, label = { it.label })
            }
            if (vm.hasProgress) ThinProgress(vm.progress, Modifier.width(320.dp))
            vm.endsAt?.let { T(it, style = Nettle.type.caption, muted = true) }
            vm.tagline?.takeIf { it.isNotEmpty() }?.let { T(it, muted = true, italic = true) }
            vm.overview?.let { T(it, Modifier.widthIn(max = 820.dp), size = 15.sp, lineHeight = 24.sp) }
            vm.genres?.let { T(it, muted = true) }
        }
    }
}

/** Episode row: 260×146 thumbnail, title / meta / 3-line overview, watched toggle. */
@Composable
private fun EpisodeRowView(vm: DetailsModel, e: EpisodeRow) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val hint = animateFloatAsState(if (focused) 1f else 0f, tween(if (Nettle.motion) 150 else 0), label = "hint")
    val hintShown by remember { derivedStateOf { hint.value > 0f } }
    val shape = RoundedCornerShape(14.dp)
    Row(
        Modifier.fillMaxWidth().clip(shape).background(p.surfaceTranslucent)
            .border(2.5.dp, if (focused) p.accent else Color.Transparent, shape).padding(12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(
            Modifier.weight(1f).rememberFocus(vm, "ep:" + e.item.id).clickable(source, indication = null) { e.play() },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(Modifier.size(260.dp, 146.dp).clip(RoundedCornerShape(10.dp)).background(p.surfaceHigh)) {
                NetImage(e.thumbUrl, Modifier.fillMaxSize())
                if (e.hasProgress) ThinProgress(e.progress, Modifier.align(Alignment.BottomCenter).fillMaxWidth().padding(8.dp))
                if (focused || hintShown) Box(Modifier.fillMaxSize().graphicsLayer { alpha = hint.value }.background(Color(0x55000000)), contentAlignment = Alignment.Center) {
                    Icon(Icons.Play, size = 34.dp, tint = Color.White)
                }
            }
            Column(Modifier.weight(1f).padding(start = 20.dp, top = 4.dp, end = 12.dp, bottom = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                T(e.title, style = Nettle.type.h3, maxLines = 1)
                if (e.meta.isNotEmpty()) T(e.meta, style = Nettle.type.caption, muted = true)
                e.overview?.let { T(it, muted = true, maxLines = 3) }
            }
        }
        IconButton(Icons.Check, { e.togglePlayed(vm.scope) }, Modifier.rememberFocus(vm, "epw:" + e.item.id), active = e.isPlayed)
    }
}

@Composable
private fun PersonView(vm: DetailsModel, person: PersonCard) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused && Nettle.motion) 1.045f else 1f, tween(160), label = "lift")
    Column(
        Modifier.width(112.dp).graphicsLayer { scaleX = scale; scaleY = scale }
            .rememberFocus(vm, "person:" + person.p.id + person.role).clickable(source, indication = null) { person.open() },
        horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Box(Modifier.size(104.dp).clip(CircleShape).background(p.surfaceHigh).border(2.5.dp, if (focused) p.accent else Color.Transparent, CircleShape),
            contentAlignment = Alignment.Center) {
            T(person.initial, size = 32.sp, weight = FontWeight.Bold, muted = true)
            NetImage(person.imageUrl, Modifier.fillMaxSize())
        }
        T(person.name, weight = FontWeight.SemiBold, maxLines = 1, align = androidx.compose.ui.text.style.TextAlign.Center)
        T(person.role, style = Nettle.type.caption, muted = true, maxLines = 1, align = androidx.compose.ui.text.style.TextAlign.Center)
    }
}

@Composable
private fun CardRowSection(title: String, cards: List<MediaCard>, vm: DetailsModel, state: androidx.compose.foundation.lazy.LazyListState) {
    Column(Modifier.padding(top = 10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        T(title, style = Nettle.type.h3)
        KeepClear(before = 24.dp, after = 80.dp) {
            LazyRow(state = state, contentPadding = PaddingValues(top = 10.dp, bottom = 26.dp), horizontalArrangement = Arrangement.spacedBy(18.dp)) {
                items(cards, key = { it.item.id }) { MediaCardView(it, page = vm) }
            }
        }
    }
}
