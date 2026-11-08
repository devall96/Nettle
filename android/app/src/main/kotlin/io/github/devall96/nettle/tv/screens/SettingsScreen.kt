package io.github.devall96.nettle.tv.screens

import android.view.KeyEvent as AKey
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.focusable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.theme.ThemePalette
import io.github.devall96.nettle.tv.ui.*
import io.github.devall96.nettle.tv.vm.Choice
import io.github.devall96.nettle.tv.vm.SettingsModel

/** SettingsView: panels for Appearance, Playback, Accounts, Seerr, NostalgiaTV, plugins and About. */
@Composable
fun SettingsScreen(vm: SettingsModel) {
    val p = Nettle.colors
    LaunchedEffect(Unit) {
        kotlinx.coroutines.delay(50)
        runCatching { (vm.lastFocusKey?.let { vm.focusRequesters[it] } ?: vm.requester("theme:" + vm.theme)).requestFocus() }
    }
    KeepClear(before = 80.dp, after = 80.dp) {
        Column(
            Modifier.fillMaxSize().verticalScroll(vm.scroll).padding(start = 56.dp, top = 40.dp, end = 56.dp, bottom = 60.dp).widthIn(max = 1100.dp),
            verticalArrangement = Arrangement.spacedBy(22.dp),
        ) {
            T("Settings", style = Nettle.type.h1)

            // Appearance
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                    SectionLabel("APPEARANCE")
                    // 220-wide swatches, as many per line as fit (WrapPanel)
                    val perRow = maxOf(1, ((minOf(1100, io.github.devall96.nettle.tv.vm.Main.canvasWidth - 80 - 112) - 48 + 16) / (220 + 16)))
                    vm.themes.chunked(perRow).forEach { line ->
                        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) { line.forEach { ThemeSwatch(vm, it) } }
                    }
                    ToggleRow("Reduce motion", vm.reduceMotion, { vm.changeReduceMotion(it) }, Modifier.rememberFocus(vm, "motion"),
                        hint = "Skips page slides, fades, card lifts and the rotating Home banner")
                    SettingRow("Interface size", "How big everything is on the TV. Standard has the same proportions as the Windows app.") {
                        Dropdown(vm.interfaceSizes, vm.interfaceSizes.firstOrNull { it.value == vm.interfaceWidth }, { vm.changeInterfaceWidth(it.value) },
                            minWidth = 200.dp, label = { it.label })
                    }
                }
            }

            // Playback
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    SectionLabel("PLAYBACK", Modifier.padding(bottom = 8.dp))
                    Box(Modifier.fillMaxWidth().padding(bottom = 8.dp).clip(RoundedCornerShape(10.dp)).background(p.accentSoft).padding(14.dp, 10.dp)) {
                        T("Nettle always direct-plays the original file. It never asks your server to transcode or remux, so quality is untouched and server CPU stays idle.")
                    }
                    ToggleRow("Audio passthrough (AV receiver)", vm.audioPassthrough, { vm.changePassthrough(it) },
                        hint = "Sends Dolby (incl. Atmos and TrueHD) and DTS (incl. DTS-HD and DTS:X) untouched to your TV, soundbar or AV receiver over HDMI, when they accept it. Off: this box decodes the sound itself; formats it can't decode fall back to another audio track. Applies from the next video.")
                    SettingRow("Match frame rate", "Switches the TV to the video's frame rate (24p films without judder), and optionally its resolution") {
                        Dropdown(vm.frameRateChoices, vm.frameRateChoices.first { it.value == vm.frameRate }, { vm.changeFrameRate(it.value) }, minWidth = 200.dp, label = { it.label })
                    }
                    ToggleRow("Auto-play next episode", vm.autoPlayNext, { vm.changeAutoPlayNext(it) }, hint = "Shows an Up Next card during the credits")
                    ToggleRow("Ask \"Are you still watching?\"", vm.stillWatching, { vm.changeStillWatching(it) },
                        hint = "Pauses before the next episode after several in a row with no input, and pauses Live TV after 4 hours with no input. Turn off to never be asked.")
                    if (vm.stillWatching) SettingRow("Episodes before asking", "Auto-played episodes in a row before the question appears") {
                        Stepper(vm.stillWatchingEpisodes, { vm.changeStillWatchingEpisodes(it) }, 1)
                    }
                    SegmentRow(vm, "Intros", "From the Intro Skipper plugin / Jellyfin media segments", vm.introAction) { vm.changeIntro(it) }
                    SegmentRow(vm, "Recaps", null, vm.recapAction) { vm.changeRecap(it) }
                    SegmentRow(vm, "Credits", null, vm.creditsAction) { vm.changeCredits(it) }
                    SegmentRow(vm, "Previews", null, vm.previewAction) { vm.changePreview(it) }
                    SegmentRow(vm, "Commercials", null, vm.commercialAction) { vm.changeCommercial(it) }
                    SettingRow("Preferred audio language", "ISO code, e.g. eng, jpn (blank = your Jellyfin profile's choice, else the file default)") {
                        TvTextField(vm.audioLanguage, { vm.audioLanguage = it }, Modifier.width(200.dp), onSubmit = { vm.commitAudioLanguage() })
                    }
                    SettingRow("Preferred subtitle language", "ISO code (blank = your Jellyfin profile's choice, else the file default)") {
                        TvTextField(vm.subtitleLanguage, { vm.subtitleLanguage = it }, Modifier.width(200.dp), onSubmit = { vm.commitSubtitleLanguage() })
                    }
                    SettingRow("Subtitles turn on", "When a subtitle track is picked automatically. You can always choose one while watching.") {
                        Dropdown(vm.subtitleModes, vm.subtitleModes.firstOrNull { it.value == vm.subtitleMode } ?: vm.subtitleModes[0], { vm.changeSubtitleMode(it.value) },
                            minWidth = 200.dp, label = { it.label })
                    }
                    SettingRow("Network buffer (seconds)", "How far ahead Nettle downloads. Raise it if playback stutters over Wi-Fi") {
                        Stepper(vm.networkBuffer, { vm.changeNetworkBuffer(it) }, 5)
                    }
                    SettingRow("Seek step (seconds)", null) { Stepper(vm.seekStep, { vm.changeSeekStep(it) }, 5) }
                }
            }

            // Accounts
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    SectionLabel("ACCOUNTS")
                    T(vm.userText, style = Nettle.type.h3)
                    T(vm.serverText, muted = true)
                    ToggleRow("Sign in automatically", vm.autoLogin, { vm.changeAutoLogin(it) }, hint = "Skip the profile picker and resume the last profile on launch")
                    T("Saved profiles on this TV (tokens encrypted with a key in the Android Keystore):", style = Nettle.type.caption, muted = true)
                    vm.accounts.forEach { a ->
                        Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                T(a.name, weight = FontWeight.SemiBold)
                                T(a.server, style = Nettle.type.caption, muted = true)
                            }
                            TextButton("Switch", { vm.switchAccount(a) }, Modifier.padding(end = 8.dp))
                            TextButton("Forget", { vm.forgetAccount(a) }, kind = ButtonKind.Ghost)
                        }
                    }
                    TextButton("Switch profile / add account", { vm.switchUser() })
                }
            }

            // Seerr
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    SectionLabel("SEERR (REQUESTS)")
                    T("Use an API key, or sign in with your Jellyfin username and password (only the resulting session is saved, encrypted).", muted = true)
                    TvTextField(vm.seerrUrl, { vm.seerrUrl = it }, Modifier.fillMaxWidth(), placeholder = "http://seerr.local:5055",
                        keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri)
                    TvTextField(vm.seerrApiKey, { vm.seerrApiKey = it }, Modifier.fillMaxWidth(), placeholder = "API key (optional)", password = true)
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        TvTextField(vm.seerrUser, { vm.seerrUser = it }, Modifier.weight(1f), placeholder = "…or Jellyfin username")
                        TvTextField(vm.seerrPassword, { vm.seerrPassword = it }, Modifier.weight(1f), placeholder = "Password", password = true)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                        TextButton("Save & test", { vm.saveSeerr() }, kind = ButtonKind.Primary)
                        vm.seerrStatus?.let { T(it, muted = true) }
                    }
                    ToggleRow("Load posters directly from TMDB", vm.allowDirectTmdb, { vm.changeAllowDirectTmdb(it) },
                        hint = "Off (recommended): posters come through your own Seerr server's image proxy, so TMDB never sees your IP or what you browse. Turn on only if posters don't show up.")
                }
            }

            // NostalgiaTV
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    SectionLabel("NOSTALGIATV (LIVE TV)")
                    T("Nettle reads the channel list and guide from NostalgiaTV (port 19850 by default). Only live channels are shown, never On Demand.", muted = true)
                    ToggleRow("Play original files (full quality)", vm.ntvDirectPlay, { vm.changeNtvDirectPlay(it) },
                        hint = "Tunes each channel by playing the scheduled episode or movie straight from your Jellyfin server at the live position: nothing is re-encoded. Turn off to use NostalgiaTV's own broadcast feed, which NostalgiaTV re-encodes.")
                    TvTextField(vm.ntvUrl, { vm.ntvUrl = it }, Modifier.fillMaxWidth(), placeholder = "http://nostalgiatv.local:19850",
                        keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri)
                    TvTextField(vm.ntvApiKey, { vm.ntvApiKey = it }, Modifier.fillMaxWidth(), placeholder = "API key (optional)", password = true)
                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                        TextButton("Save & test", { vm.saveNtv() }, kind = ButtonKind.Primary)
                        vm.ntvStatus?.let { T(it, muted = true) }
                    }
                }
            }

            // Server plugins
            if (vm.pluginsLoaded) Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    SectionLabel("SERVER PLUGINS")
                    vm.plugins.forEach { pl ->
                        Row(Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                T(pl.name, weight = FontWeight.SemiBold)
                                T(pl.version, style = Nettle.type.caption, muted = true)
                            }
                            T(pl.support, style = Nettle.type.caption, muted = true)
                        }
                    }
                }
            }

            // Updates (same source, toggle and wording as the Windows / Linux apps)
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    SectionLabel("UPDATES")
                    T(vm.installKind, weight = FontWeight.SemiBold)
                    T("Nettle never checks for updates on its own unless you turn that on. It only ever contacts the source below: Nettle's GitHub Releases (filled in for you) or another GitHub repository. Each update is the newest release's Nettle-AndroidTV.apk, and the TV asks you before installing it.", muted = true)
                    TvTextField(vm.updateSource, { vm.updateSource = it }, Modifier.fillMaxWidth(), placeholder = "https://github.com/devall96/Nettle",
                        keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri, onSubmit = { vm.commitUpdateSource() })
                    ToggleRow("Check automatically when Nettle starts", vm.autoCheckUpdates, { vm.changeAutoCheckUpdates(it) },
                        hint = "Off by default. You'll be asked before anything is installed.")
                    Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                        TextButton("Check now", { if (!vm.isUpdating) vm.checkUpdates() })
                        if (vm.availableUpdate != null)
                            TextButton("Download & install", { if (!vm.isUpdating) vm.installUpdate() }, kind = ButtonKind.Primary)
                        vm.updateStatus?.let { T(it, muted = true) }
                    }
                }
            }

            // About
            Panel(Modifier.fillMaxWidth()) {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    SectionLabel("ABOUT")
                    T(vm.versionText, weight = FontWeight.SemiBold)
                    T("Nettle is an independent client and is not affiliated with Jellyfin, Seerr or NostalgiaTV. Playback is powered by AndroidX Media3 (Apache-2.0). Open-source licenses are listed in THIRD_PARTY_NOTICES.md in Nettle's repository.", muted = true)
                    TextButton("Clear image cache", { vm.clearImageCache() })
                }
            }
        }
    }
}

/** Grid.row: title + hint on the left, the control on the right. */
@Composable
private fun SettingRow(title: String, hint: String?, control: @Composable () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(start = 10.dp, end = 16.dp)) {
            T(title, weight = FontWeight.SemiBold)
            if (hint != null) T(hint, style = Nettle.type.caption, muted = true)
        }
        control()
    }
}

@Composable
private fun SegmentRow(vm: SettingsModel, title: String, hint: String?, value: io.github.devall96.nettle.core.models.SegmentAction,
                       onChange: (io.github.devall96.nettle.core.models.SegmentAction) -> Unit) =
    SettingRow(title, hint) {
        Dropdown(vm.segmentChoices, vm.segmentChoices.first { it.value == value }, { onChange(it.value) }, minWidth = 200.dp, label = { it.label })
    }

/** NumericUpDown on a TV: Left / Right (or the − / + buttons) change the value. */
@Composable
private fun Stepper(value: Int, onChange: (Int) -> Unit, step: Int) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val shape = RoundedCornerShape(12.dp)
    Row(
        Modifier.width(200.dp).clip(shape).background(if (focused) p.surfaceHigh else p.surface).border(if (focused) 2.dp else 1.dp, if (focused) p.accent else p.border, shape)
            .onPreviewKeyEvent { e ->
                val c = e.nativeKeyEvent.keyCode
                if (c != AKey.KEYCODE_DPAD_LEFT && c != AKey.KEYCODE_DPAD_RIGHT) return@onPreviewKeyEvent false
                if (e.type == KeyEventType.KeyDown) onChange(value + if (c == AKey.KEYCODE_DPAD_RIGHT) step else -step)
                true
            }
            .focusable(interactionSource = source).padding(horizontal = 6.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(Icons.ChevronLeft, size = 18.dp, tint = p.textMuted)
        T(value.toString(), Modifier.weight(1f), size = 15.sp, align = androidx.compose.ui.text.style.TextAlign.Center)
        Icon(Icons.ChevronRight, size = 18.dp, tint = p.textMuted)
    }
}

/** A 220-wide preview of a theme, drawn in that theme's own colours. */
@Composable
private fun ThemeSwatch(vm: SettingsModel, t: ThemePalette) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val scale by animateFloatAsState(if (focused && Nettle.motion) 1.045f else 1f, tween(160), label = "lift")
    val selected = vm.theme == t.key
    val shape = RoundedCornerShape(14.dp)
    Column(
        Modifier.width(220.dp).graphicsLayer { scaleX = scale; scaleY = scale }.clip(shape).background(t.background)
            .border(2.5.dp, if (focused) p.accent else if (selected) p.accent.copy(alpha = 0.8f) else Color.Transparent, shape)
            .rememberFocus(vm, "theme:" + t.key).clickable(source, indication = null) { vm.applyTheme(t.key) }.padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(Modifier.height(64.dp)) {
            Column(Modifier.weight(1f).fillMaxHeight().padding(end = 8.dp).clip(RoundedCornerShape(8.dp)).background(t.surfaceHigh).padding(10.dp),
                verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Box(Modifier.size(70.dp, 8.dp).clip(RoundedCornerShape(4.dp)).background(t.text))
                Box(Modifier.size(50.dp, 6.dp).clip(RoundedCornerShape(3.dp)).background(t.textMuted))
                Box(Modifier.size(44.dp, 14.dp).clip(RoundedCornerShape(7.dp)).background(t.accent))
            }
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Box(Modifier.size(26.dp).clip(CircleShape).background(t.accent))
                Box(Modifier.size(26.dp).clip(CircleShape).background(t.accent2))
            }
        }
        Column {
            T(t.name, weight = FontWeight.Bold, color = t.text)
            T(t.tagline, size = 12.sp, color = t.textMuted, maxLines = 1)
        }
    }
}
