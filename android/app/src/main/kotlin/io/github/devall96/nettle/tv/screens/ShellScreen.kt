package io.github.devall96.nettle.tv.screens

import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.focusGroup
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.layout
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusProperties
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.*

/** ShellView: the page area, the floating back button and the rail that widens over the page. */
@Composable
fun ShellScreen(shell: Shell) {
    val p = Nettle.colors
    val motion = Nettle.motion
    var railFocused by remember { mutableStateOf(false) }
    val expanded = railFocused
    val railWidth = animateDpAsState(if (expanded) 250.dp else 80.dp, tween(if (motion) 200 else 0, easing = CubicOut), label = "rail")
    val labelAlphaState = animateFloatAsState(if (expanded) 1f else 0f, tween(if (motion) 150 else 0), label = "labels")
    val labelAlpha: () -> Float = remember { { labelAlphaState.value } }
    val selectedNav = remember { FocusRequester() }

    Box(Modifier.fillMaxSize()) {
        // Page area (cross-fades 0.18 s between pages)
        Box(Modifier.fillMaxSize().padding(start = 80.dp)) {
            val page = shell.current
            if (motion) Crossfade(page, animationSpec = tween(180), label = "page") { PageHost(it) } else PageHost(page)
        }

        // Back button (floating). The remote's Back key does this on TV, so it isn't a focus stop.
        if (shell.canGoBack) Box(
            Modifier.padding(start = 100.dp, top = 36.dp).zIndex(5f).padding(10.dp),
        ) { Icon(Icons.Back, tint = if (Nettle.colors.isLight) Nettle.colors.text else Color.White) }

        // Navigation rail
        Box(
            Modifier.zIndex(10f).fillMaxHeight()
                // The animated width is read while measuring only: widening the rail never recomposes it.
                .layout { m, c -> val w = railWidth.value.roundToPx(); val pl = m.measure(c.copy(minWidth = w, maxWidth = w)); layout(pl.width, pl.height) { pl.place(0, 0) } }
                .then(if (expanded) Modifier.drawBehind {
                    // BoxShadow 12 0 40 0 #66000000
                    drawRect(Brush.horizontalGradient(listOf(Color(0x66000000), Color.Transparent), startX = size.width, endX = size.width + 52.dp.toPx()),
                        topLeft = Offset(size.width, 0f), size = androidx.compose.ui.geometry.Size(52.dp.toPx(), size.height))
                } else Modifier)
                .background(p.backgroundAlt)
                .drawBehind { drawRect(p.border, topLeft = Offset(size.width - 1.dp.toPx(), 0f), size = androidx.compose.ui.geometry.Size(1.dp.toPx(), size.height)) }
                .onFocusChanged { railFocused = it.hasFocus }
                .focusProperties { onEnter = { selectedNav.requestFocus() } }
                .focusGroup(),
        ) {
            Column(Modifier.fillMaxSize().padding(start = 12.dp, top = 40.dp, end = 12.dp, bottom = 18.dp)) {
                Row(Modifier.padding(start = 10.dp, bottom = 26.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    BrandMark(0.75f, showWordmark = false)
                    Column(Modifier.graphicsLayer { alpha = labelAlpha() }) {
                        T("Nettle", size = 20.sp, weight = FontWeight.ExtraBold, maxLines = 1)
                        T(shell.versionText, Modifier.padding(start = 1.dp), style = Nettle.type.caption, muted = true, maxLines = 1)
                    }
                }
                Column(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
                    val key = shell.current?.navKey
                    shell.navTop.forEach { NavButton(it, it.key == key, labelAlpha, if (it.key == key) selectedNav else null) }
                    if (shell.navLibraries.isNotEmpty()) {
                        Box(Modifier.padding(horizontal = 8.dp, vertical = 10.dp).fillMaxWidth().height(1.dp).background(p.border))
                        shell.navLibraries.forEach { NavButton(it, it.key == key, labelAlpha, if (it.key == key) selectedNav else null) }
                    }
                    Box(Modifier.padding(horizontal = 8.dp, vertical = 10.dp).fillMaxWidth().height(1.dp).background(p.border))
                    shell.navBottom.forEach { NavButton(it, it.key == key, labelAlpha, if (it.key == key) selectedNav else null) }
                }
                // Profile
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    if (shell.isAccountMenuOpen) Column(Modifier.padding(bottom = 6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        RailButton(Icons.Swap, "Switch profile", false, labelAlpha) { shell.switchUser() }
                        RailButton(Icons.Logout, "Sign out & forget", false, labelAlpha) { shell.signOut() }
                    }
                    key(shell.accountVersion) {
                        NButton({ shell.toggleAccountMenu() }, Modifier.fillMaxWidth(), ButtonKind.Nav, padding = PaddingValues(8.dp)) {
                            Box(Modifier.size(36.dp).clip(CircleShape).background(p.accentGradient), contentAlignment = Alignment.Center) {
                                T(shell.userInitial, weight = FontWeight.Bold, color = p.onAccent)
                                NetImage(shell.avatarUrl, Modifier.fillMaxSize())
                            }
                            Spacer(Modifier.width(14.dp))
                            Column(Modifier.graphicsLayer { alpha = labelAlpha() }) {
                                T(shell.userName, weight = FontWeight.SemiBold, maxLines = 1)
                                T(shell.serverName, style = Nettle.type.caption, muted = true, maxLines = 1)
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun NavButton(item: NavItem, selected: Boolean, labelAlpha: () -> Float, requester: FocusRequester?) =
    RailButton(item.icon(), item.label, selected, labelAlpha, requester) { item.go() }

/** Button.nav: icon 22, 18 apart from a 15-unit label that fades in as the rail widens. */
@Composable
private fun RailButton(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, selected: Boolean, labelAlpha: () -> Float,
                       requester: FocusRequester? = null, onClick: () -> Unit) {
    NButton(onClick, Modifier.fillMaxWidth().padding(vertical = 2.dp), ButtonKind.Nav, selected = selected, focusRequester = requester) { fg ->
        Icon(icon, tint = fg)
        Spacer(Modifier.width(18.dp))
        BT(label, Modifier.width(150.dp).graphicsLayer { alpha = labelAlpha() }, size = 15.sp)
    }
}

@Composable
private fun PageHost(page: Page?) {
    when (page) {
        null -> {}
        is HomeModel -> HomeScreen(page)
        is DetailsModel -> DetailsScreen(page)
        is LibraryModel -> LibraryScreen(page)
        is SearchModel -> SearchScreen(page)
        is LiveTvModel -> LiveTvScreen(page)
        is RequestsModel -> RequestsScreen(page)
        is WatchTogetherModel -> WatchTogetherScreen(page)
        is SettingsModel -> SettingsScreen(page)
        else -> PlaceholderScreen(page)
    }
}

@Composable
private fun PlaceholderScreen(page: Page) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        T("This page is still being ported: ${page::class.simpleName}", muted = true)
    }
}
