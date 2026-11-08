package io.github.devall96.nettle.tv.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.*

/** Grid.page with Margin 48,40 and the brand mark top-left (shared by the three sign-in screens). */
@Composable
private fun SignInFrame(topRight: @Composable () -> Unit = {}, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxSize().padding(horizontal = 48.dp, vertical = 40.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            BrandMark(0.9f)
            Spacer(Modifier.weight(1f))
            topRight()
        }
        Column(Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState()), horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center, content = content)
    }
}

/** Round profile picture with the initial behind it, lifting when focused (Button.card + Border.frame). */
@Composable
private fun AvatarButton(size: Dp, initial: String, initialSize: Float, url: String?, onClick: () -> Unit, focusRequester: FocusRequester? = null,
                         busy: Boolean = false) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused && Nettle.motion) 1.045f else 1f, tween(160), label = "lift")
    Box(
        Modifier.size(size).graphicsLayer { scaleX = scale; scaleY = scale }.clip(CircleShape).background(p.accentGradient)
            .border(2.5.dp, if (focused) p.accent else Color.Transparent, CircleShape)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .clickable(source, indication = null, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        T(initial, size = initialSize.sp, weight = FontWeight.Bold, color = p.onAccent)
        NetImage(url, Modifier.fillMaxSize())
        if (busy) Box(Modifier.fillMaxSize().background(Color(0x99000000)), contentAlignment = Alignment.Center) { Spinner(size = 36.dp) }
    }
}

@Composable
fun AccountPickerScreen(vm: AccountPickerModel) {
    val p = Nettle.colors
    val first = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { first.requestFocus() } }
    Column(Modifier.fillMaxSize().padding(horizontal = 48.dp, vertical = 40.dp)) {
        BrandMark(0.9f)
        Column(Modifier.fillMaxWidth().weight(1f), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
            T("Who's watching?", style = Nettle.type.h1)
            Spacer(Modifier.height(36.dp))
            Row(horizontalArrangement = Arrangement.Center) {
                vm.accounts.forEachIndexed { i, tile ->
                    Column(Modifier.padding(18.dp).width(150.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        AvatarButton(132.dp, tile.initial, 52f, tile.avatarUrl, { vm.signIn(tile) }, if (i == 0) first else null, tile.isSigningIn)
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            T(tile.name, style = Nettle.type.h3, maxLines = 1)
                            T(tile.server, style = Nettle.type.caption, muted = true, maxLines = 1)
                        }
                        if (vm.isEditing) TextButton("Forget", { vm.forget(tile) }, kind = ButtonKind.Ghost, padding = PaddingValues(12.dp, 6.dp))
                    }
                }
            }
            ErrorText(vm.error)
            Spacer(Modifier.height(36.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                NButton({ vm.addAccount() }, kind = ButtonKind.Primary) { fg ->
                    Icon(Icons.Plus, size = 16.dp, tint = fg)
                    Spacer(Modifier.width(8.dp))
                    BT("Add account")
                }
                TextButton("Manage", { vm.toggleEdit() })
            }
        }
        Row(Modifier.align(Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ToggleRow("Sign in automatically to the last profile on launch", vm.autoLogin, { vm.changeAutoLogin(it) }, Modifier.width(560.dp))
        }
    }
}

@Composable
fun ServerSelectScreen(vm: ServerSelectModel) {
    val p = Nettle.colors
    val address = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { address.requestFocus() } }
    SignInFrame(topRight = {
        if (vm.canGoBack) NButton({ vm.back() }, kind = ButtonKind.Ghost) { fg ->
            Icon(Icons.Back, size = 16.dp, tint = fg); Spacer(Modifier.width(8.dp)); BT("Profiles")
        }
    }) {
        Panel(Modifier.width(520.dp), padding = PaddingValues(36.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(18.dp)) {
                Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    T("Connect to your server", style = Nettle.type.h2)
                    T("Pick http or https and type your server's IP address. Live TV (NostalgiaTV) and Requests (Seerr) on the same machine are set up for you after you sign in.", muted = true)
                }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        TextButton("http", { vm.useHttps(false) }, kind = ButtonKind.Chip, selected = !vm.isHttps, padding = PaddingValues(14.dp, 7.dp))
                        TextButton("https", { vm.useHttps(true) }, kind = ButtonKind.Chip, selected = vm.isHttps, padding = PaddingValues(14.dp, 7.dp))
                    }
                    TvTextField(vm.address, { vm.address = it }, Modifier.weight(1f), placeholder = "192.168.1.20",
                        keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri, focusRequester = address, onSubmit = { vm.connect() })
                }
                T(vm.portHint, Modifier.offset(y = (-8).dp), style = Nettle.type.caption, muted = true)
                ErrorText(vm.error)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (vm.isBusy) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Spinner(size = 20.dp); T("Connecting…", muted = true)
                    }
                    Spacer(Modifier.weight(1f))
                    TextButton("Connect", { vm.connect() }, kind = ButtonKind.Primary)
                }
                Box(Modifier.fillMaxWidth().padding(vertical = 6.dp).height(1.dp).background(p.border))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    SectionLabel("ON YOUR NETWORK", Modifier.weight(1f))
                    IconButton(Icons.Refresh, { vm.scan() }, size = 18.dp)
                }
                if (vm.isScanning) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Spinner(size = 18.dp); T("Looking for servers…", muted = true)
                }
                Column {
                    vm.discovered.forEach { s ->
                        NButton({ vm.connectTo(s.address ?: "") }, Modifier.fillMaxWidth().padding(vertical = 2.dp), kind = ButtonKind.Ghost) {
                            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                                Icon(Icons.Server, tint = p.accent)
                                Column {
                                    T(s.name ?: "Jellyfin", weight = FontWeight.SemiBold)
                                    T(s.address ?: "", style = Nettle.type.caption, muted = true)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun LoginScreen(vm: LoginModel) {
    val p = Nettle.colors
    val firstUser = remember { FocusRequester() }
    val userBox = remember { FocusRequester() }
    val passBox = remember { FocusRequester() }
    LaunchedEffect(vm.showManualForm, vm.users.size) {
        runCatching { if (vm.showManualForm) (if (vm.username.isEmpty()) userBox else passBox).requestFocus() else if (vm.users.isNotEmpty()) firstUser.requestFocus() }
    }
    LaunchedEffect(vm.passwordFocusRequests) { if (vm.passwordFocusRequests > 0) runCatching { passBox.requestFocus() } }
    SignInFrame(topRight = {
        NButton({ vm.changeServer() }, kind = ButtonKind.Ghost) { fg ->
            Icon(Icons.Swap, size = 16.dp, tint = fg); Spacer(Modifier.width(8.dp)); BT("Change server")
        }
    }) {
        Column(Modifier.widthIn(max = 900.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(26.dp)) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                T(vm.serverName, style = Nettle.type.h1)
                T(vm.serverDetail, style = Nettle.type.caption, muted = true)
            }
            if (vm.isInsecureConnection) Box(
                Modifier.widthIn(max = 620.dp).clip(RoundedCornerShape(12.dp)).background(Color(0x33F59E0B))
                    .border(1.dp, Color(0xFFF59E0B), RoundedCornerShape(12.dp)).padding(16.dp, 12.dp),
            ) {
                T("This server uses plain http:// over the internet, so your password and session would be sent unencrypted. Use an https:// address (or connect over your LAN / Tailscale) if you can.", color = p.warning)
            }
            vm.message?.let { Panel(padding = PaddingValues(16.dp, 12.dp)) { T(it, color = p.accent) } }

            if (!vm.showManualForm) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(18.dp)) {
                    Row {
                        vm.users.forEachIndexed { i, u ->
                            Column(Modifier.padding(14.dp).width(124.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                                AvatarButton(108.dp, u.initial, 42f, u.avatarUrl, { vm.selectUser(u) }, if (i == 0) firstUser else null)
                                T(u.name, weight = FontWeight.SemiBold, maxLines = 1)
                            }
                        }
                    }
                    TextButton("Sign in with a different user", { vm.showManual() })
                }
            } else {
                Panel(Modifier.width(440.dp), padding = PaddingValues(32.dp)) {
                    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                        T("Sign in", style = Nettle.type.h3)
                        TvTextField(vm.username, { vm.username = it }, Modifier.fillMaxWidth(), placeholder = "Username", focusRequester = userBox,
                            onSubmit = { runCatching { passBox.requestFocus() } })
                        TvTextField(vm.password, { vm.password = it }, Modifier.fillMaxWidth(), placeholder = "Password", password = true,
                            focusRequester = passBox, onSubmit = { vm.signIn() })
                        CheckRow("Remember me and sign in automatically", vm.rememberMe, { vm.rememberMe = it })
                        ErrorText(vm.error)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            if (vm.hasUsers) TextButton("Back", { vm.showUsers() }, kind = ButtonKind.Ghost)
                            Spacer(Modifier.weight(1f))
                            if (vm.isBusy) { Spinner(size = 22.dp); Spacer(Modifier.width(12.dp)) }
                            TextButton("Sign in", { vm.signIn() }, kind = ButtonKind.Primary)
                        }
                        T("Your password is never stored. Nettle keeps only the session token Jellyfin issues, encrypted with a key kept in this TV's secure hardware (Android Keystore).",
                            style = Nettle.type.caption, muted = true)
                    }
                }
            }

            if (vm.quickConnectAvailable) {
                val code = vm.quickConnectCode
                val useQc = remember { FocusRequester() }
                var panelShown by remember { mutableStateOf(false) }
                // Cancel (or the code expiring) removed the panel with focus in it: give focus back to "Use Quick Connect".
                LaunchedEffect(code == null) {
                    if (code != null) panelShown = true
                    else if (panelShown) { panelShown = false; kotlinx.coroutines.delay(30); runCatching { useQc.requestFocus() } }
                }
                if (code == null) NButton({ vm.startQuickConnect() }, kind = ButtonKind.Ghost, focusRequester = useQc) { fg ->
                    Icon(Icons.Key, size = 18.dp, tint = p.accent); Spacer(Modifier.width(8.dp)); BT("Use Quick Connect")
                } else Panel(padding = PaddingValues(28.dp, 20.dp)) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        T("Enter this code in Quick Connect on a signed-in device", muted = true)
                        T(code, size = 44.sp, weight = FontWeight.ExtraBold, color = p.accent, style = Nettle.type.body.copy(letterSpacing = 8.sp))
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Spinner(size = 18.dp); T("Waiting for approval…", muted = true)
                        }
                        val cancel = remember { FocusRequester() }
                        // The "Use Quick Connect" button just disappeared: put focus on Cancel, not wherever it lands.
                        LaunchedEffect(code) { runCatching { cancel.requestFocus() } }
                        TextButton("Cancel", { vm.cancelQuickConnect() }, kind = ButtonKind.Ghost, focusRequester = cancel)
                    }
                }
            }
        }
    }
}
