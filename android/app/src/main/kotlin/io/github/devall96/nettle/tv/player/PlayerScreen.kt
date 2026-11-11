package io.github.devall96.nettle.tv.player

import android.app.Activity
import android.view.KeyEvent as AKey
import android.view.SurfaceView
import androidx.annotation.OptIn
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.foundation.*
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.focus.focusProperties
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.key.*
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.CaptionStyleCompat
import androidx.media3.ui.SubtitleView
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.vm.Main
import io.github.devall96.nettle.tv.theme.Icons
import io.github.devall96.nettle.tv.theme.Nettle
import io.github.devall96.nettle.tv.ui.*

private val White = Color.White

/** PlayerView: the video on a SurfaceView, subtitles, and the OSD laid out as on Windows. */
@OptIn(UnstableApi::class)
@Composable
fun PlayerScreen(vm: PlayerModel) {
    val p = Nettle.colors
    val activity = LocalContext.current as? Activity
    val view = LocalView.current
    val root = remember { FocusRequester() }
    val seekBar = remember { FocusRequester() }
    val playPause = remember { FocusRequester() }
    val tracksFirst = remember { FocusRequester() }
    val continueBtn = remember { FocusRequester() }
    var anyFocus by remember { mutableStateOf(false) }
    // The 1-dp key catcher keeps focus when the controls appear (making it unfocusable doesn't take focus away).
    var catcherFocused by remember { mutableStateOf(false) }

    // Screen stays on only while something is actually playing.
    DisposableEffect(vm.isPlaying) { view.keepScreenOn = vm.isPlaying; onDispose { view.keepScreenOn = false } }
    // Refresh rate (and optionally resolution) follow the video; restored when the player closes.
    DisposableEffect(vm) {
        vm.engine.onFrameRate = { fps ->
            if (activity != null) {
                FrameRate.apply(activity, fps, vm.engine.videoWidth, vm.engine.videoHeight, AppServices.settings.current.playback.frameRateMatching)
            }
        }
        onDispose { vm.engine.onFrameRate = null; activity?.let { FrameRate.restore(it) } }
    }
    // Focus: the hidden-OSD catcher, or the OSD's main control, or an open panel.
    LaunchedEffect(vm.isOsdVisible, vm.isTracksOpen, vm.isSubtitleSearchOpen, vm.isStillWatchingVisible) {
        // The target may still be animating in (not attached yet, so requestFocus throws): retry briefly.
        // A single early attempt used to fail silently and leave nothing focused, so Left / Right did nothing.
        // requestFocus can also be ignored without an error while the controls animate in, so check where focus
        // really went: off the hidden catcher (or onto it, when the controls are hidden).
        val wantCatcher = !vm.isStillWatchingVisible && !vm.isTracksOpen && !vm.isSubtitleSearchOpen && !vm.isOsdVisible
        repeat(20) {
            kotlinx.coroutines.delay(if (it == 0) 30L else 50L)
            runCatching {
                when {
                    vm.isStillWatchingVisible -> continueBtn.requestFocus()
                    vm.isTracksOpen || vm.isSubtitleSearchOpen -> tracksFirst.requestFocus()
                    vm.isOsdVisible -> if (vm.isVod) seekBar.requestFocus() else playPause.requestFocus()
                    else -> root.requestFocus()
                }
            }
            kotlinx.coroutines.delay(16)
            if (if (wantCatcher) catcherFocused else anyFocus && !catcherFocused) return@LaunchedEffect
        }
    }

    Box(
        Modifier.fillMaxSize().background(Color.Black)
            .onFocusChanged { anyFocus = it.hasFocus }
            .onPreviewKeyEvent { e ->
                // Controls showing but focus still on the hidden catcher, or nowhere (it can get lost while they animate in): Left / Right
                // scrub, as on the seek bar, and focus moves to the seek bar, instead of the keys doing nothing.
                val code = e.nativeKeyEvent.keyCode
                if (io.github.devall96.nettle.tv.BuildConfig.SYNC_TEST && (code == AKey.KEYCODE_DPAD_LEFT || code == AKey.KEYCODE_DPAD_RIGHT))
                    io.github.devall96.nettle.core.services.Log.info("key $code ${e.type} rep=${e.nativeKeyEvent.repeatCount} anyFocus=$anyFocus osd=${vm.isOsdVisible} pending=${vm.pendingSeekMs}")
                if ((!anyFocus || catcherFocused) && vm.isOsdVisible && vm.isVod && !vm.isLipSyncCheck &&
                    (code == AKey.KEYCODE_DPAD_LEFT || code == AKey.KEYCODE_DPAD_RIGHT)) {
                    vm.userActivity()
                    if (e.type == KeyEventType.KeyDown) vm.holdSeek(code == AKey.KEYCODE_DPAD_RIGHT, e.nativeKeyEvent.repeatCount)
                    else if (e.type == KeyEventType.KeyUp) vm.scrubKeyUp()
                    runCatching { seekBar.requestFocus() }
                    true
                } else handleKey(vm, e)
            },
    ) {
        // Holds focus while the OSD is hidden (keys reach handleKey above).
        val catcherOn = !vm.isOsdVisible && !vm.isTracksOpen && !vm.isSubtitleSearchOpen && !vm.isStillWatchingVisible && vm.error == null
        Box(Modifier.size(1.dp).focusRequester(root).focusProperties { canFocus = catcherOn }
            .onFocusChanged { catcherFocused = it.isFocused }.focusable())
        // ---- Video ----
        val w = vm.engine.videoWidth
        val h = vm.engine.videoHeight
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            AndroidView(
                factory = { ctx -> SurfaceView(ctx).also { it.isFocusable = false; it.isFocusableInTouchMode = false; vm.engine.exo.setVideoSurfaceView(it) } },
                modifier = (if (w > 0 && h > 0) Modifier.aspectRatio(w.toFloat() / h, matchHeightConstraintsFirst = w.toFloat() / h < 16f / 9f) else Modifier.fillMaxSize())
                    .focusProperties { canFocus = false },
                onRelease = { sv -> runCatching { vm.engine.exo.clearVideoSurfaceView(sv) } },
            )
        }
        // ---- Live TV tuning screen: covers channel / program switches until the new picture is up ----
        if (vm.isLive) TuningScreen(vm)
        // ---- Subtitles (Media3's renderer; size from Settings / the tracks panel) ----
        AndroidView(
            factory = { ctx ->
                SubtitleView(ctx).apply {
                    setStyle(CaptionStyleCompat(android.graphics.Color.WHITE, android.graphics.Color.TRANSPARENT, android.graphics.Color.TRANSPARENT,
                        CaptionStyleCompat.EDGE_TYPE_OUTLINE, android.graphics.Color.BLACK, null))
                    setApplyEmbeddedStyles(true)
                    isFocusable = false
                    descendantFocusability = android.view.ViewGroup.FOCUS_BLOCK_DESCENDANTS
                }
            },
            update = { sv ->
                sv.setCues(vm.engine.cues)
                sv.setFractionalTextSize(SubtitleView.DEFAULT_TEXT_SIZE_FRACTION * vm.subtitleScale.toFloat())
            },
            modifier = Modifier.fillMaxSize().focusProperties { canFocus = false },
        )

        // ---- Buffering ----
        if (vm.isBuffering && vm.error == null) Spinner(Modifier.align(Alignment.Center), size = 64.dp)

        // ---- OSD ----
        val osdAlpha by animateFloatAsState(if (vm.isOsdVisible) 1f else 0f, tween(if (Nettle.motion) 250 else 0), label = "osd")
        if (osdAlpha > 0f) Box(Modifier.fillMaxSize().alpha(osdAlpha)) {
            Box(Modifier.fillMaxWidth().height(190.dp).background(Brush.verticalGradient(listOf(Color(0xCC000000), Color.Transparent))))
            Row(Modifier.align(Alignment.TopStart).fillMaxWidth().padding(start = 36.dp, top = 30.dp, end = 36.dp), verticalAlignment = Alignment.CenterVertically) {
                OsdButton(Icons.Back, { vm.close() })
                Column(Modifier.weight(1f).padding(horizontal = 16.dp)) {
                    T(vm.title, size = 22.sp, weight = FontWeight.Bold, color = White, maxLines = 1)
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                        vm.subtitle?.let { T(it, Modifier.alpha(0.8f), color = White, maxLines = 1) }
                        if (vm.isVod) vm.sourceLabel?.takeIf { it.isNotEmpty() }?.let { SourcePill(it, true) }
                        vm.syncPlayLabel?.let { SourcePill(it, false) }
                    }
                }
                OsdButton(Icons.Subtitles, { vm.toggleTracks() })
            }
            Box(Modifier.align(Alignment.BottomCenter).fillMaxWidth().height(260.dp).background(Brush.verticalGradient(listOf(Color.Transparent, Color(0xE6000000)))))
            if (vm.isVod) VodControls(vm, seekBar, playPause, Modifier.align(Alignment.BottomCenter))
            else LiveControls(vm, playPause, Modifier.align(Alignment.BottomCenter))
        }

        // ---- Brief messages (chapter names, speed) ----
        vm.osdMessage?.let {
            Box(Modifier.align(Alignment.TopCenter).padding(top = 120.dp).clip(RoundedCornerShape(12.dp)).background(Color(0xCC000000)).padding(20.dp, 10.dp)) {
                T(it, size = 18.sp, weight = FontWeight.SemiBold, color = White)
            }
        }
        // ---- Settings → Lip sync check ----
        if (vm.isLipSyncCheck) Column(
            Modifier.align(Alignment.BottomCenter).padding(bottom = 56.dp).widthIn(max = 720.dp)
                .clip(RoundedCornerShape(16.dp)).background(Color(0xE6141414)).padding(28.dp, 20.dp),
            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            T("Lip sync check · ${vm.refreshKey} Hz", size = 20.sp, weight = FontWeight.Bold, color = White)
            T("A grey box blinks in the middle of the screen once a second, with a beep. Line them up so they happen at the same moment.",
                color = White, align = TextAlign.Center)
            T("Beep first? Press ▶ to play the sound later.  Box first? Press ◀ to play it earlier.",
                Modifier.alpha(0.75f), color = White, align = TextAlign.Center)
            T("◀   ${vm.audioOffsetText}   ▶", size = 30.sp, weight = FontWeight.ExtraBold, color = White)
            T("Saved for ${vm.refreshKey} Hz as you go. Back or OK when done.", Modifier.alpha(0.6f), size = 13.sp, color = White)
        }
        // ---- Between programs ----
        vm.liveInterstitial?.let { T(it, Modifier.align(Alignment.Center), size = 26.sp, weight = FontWeight.Bold, color = White, align = TextAlign.Center) }
        // (Live TV: the channel shows once, on the tuning screen and then the controls bar, not also at the top right.)
        // ---- Channel number being typed on the remote ----
        vm.typedChannel?.let {
            Box(Modifier.align(Alignment.TopEnd).padding(top = 40.dp, end = 44.dp).clip(RoundedCornerShape(14.dp)).background(Color(0xCC000000)).padding(22.dp, 14.dp)) {
                T(it + "_", size = 34.sp, weight = FontWeight.ExtraBold, color = White)
            }
        }
        // ---- Skip segment ----
        if (vm.isSkipVisible) NButton({ vm.skip() }, Modifier.align(Alignment.BottomEnd).padding(end = 48.dp, bottom = 150.dp), ButtonKind.Primary,
            padding = PaddingValues(26.dp, 14.dp)) { fg ->
            BT(vm.skipLabel ?: "Skip", size = 16.sp); Spacer(Modifier.width(10.dp)); Icon(Icons.SkipNext, size = 18.dp, tint = fg)
        }
        // ---- Up next ----
        if (vm.isUpNextVisible) UpNext(vm, Modifier.align(Alignment.BottomEnd).padding(end = 48.dp, bottom = 150.dp))
        // ---- Format badges (bottom left, above the controls) ----
        val badgeAlpha by animateFloatAsState(if (vm.isFormatBadgesVisible) 1f else 0f, tween(450, easing = CubicOut), label = "badges")
        if (badgeAlpha > 0f) Column(
            Modifier.align(Alignment.BottomStart).padding(start = 44.dp, bottom = 150.dp).alpha(badgeAlpha), verticalArrangement = Arrangement.spacedBy(10.dp),
            horizontalAlignment = Alignment.Start,
        ) {
            vm.formatBadges.forEach { Badge(it) }
        }
        // ---- Error ----
        vm.error?.let { err ->
            Column(Modifier.align(Alignment.Center).widthIn(max = 560.dp).clip(RoundedCornerShape(16.dp)).background(Color(0xDD111111)).padding(28.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)) {
                T("Playback problem", size = 20.sp, weight = FontWeight.Bold, color = White)
                T(err, Modifier.alpha(0.85f), color = White)
                val close = remember { FocusRequester() }
                LaunchedEffect(err) { runCatching { close.requestFocus() } }
                TextButton("Close", { vm.close() }, focusRequester = close)
            }
        }
        // ---- Tracks panel / find subtitles ----
        if (vm.isTracksOpen && !vm.isSubtitleSearchOpen) TracksPanel(vm, tracksFirst, Modifier.align(Alignment.TopEnd).padding(top = 100.dp, end = 36.dp))
        if (vm.isSubtitleSearchOpen) SubtitleSearchPanel(vm, tracksFirst, Modifier.align(Alignment.TopEnd).padding(top = 100.dp, end = 36.dp))
        // ---- "Are you still watching?" ----
        if (vm.isStillWatchingVisible) Box(Modifier.fillMaxSize().background(Color(0xB3000000)), contentAlignment = Alignment.Center) {
            Column(
                Modifier.widthIn(max = 520.dp).clip(RoundedCornerShape(20.dp)).background(Color(0xF0141418)).border(1.dp, Color(0x30FFFFFF), RoundedCornerShape(20.dp))
                    .padding(34.dp, 30.dp), verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                T("Are you still watching?", size = 28.sp, weight = FontWeight.ExtraBold, color = White)
                vm.stillWatchingDetail?.let { T(it, Modifier.alpha(0.8f), color = White) }
                Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    NButton({ vm.continueWatching() }, kind = ButtonKind.Primary, padding = PaddingValues(22.dp, 12.dp), focusRequester = continueBtn) { BT("Continue watching") }
                    TextButton("Stop", { vm.stopWatching() }, padding = PaddingValues(22.dp, 12.dp))
                }
                T("You can turn this question off in Settings → Playback.", Modifier.alpha(0.55f), size = 12.sp, color = White)
            }
        }
    }
}

/** Remote keys (port of PlayerViewModel.HandleKey, for a TV remote). */
private fun handleKey(vm: PlayerModel, e: KeyEvent): Boolean {
    val code = e.nativeKeyEvent.keyCode
    val down = e.type == KeyEventType.KeyDown
    val up = e.type == KeyEventType.KeyUp
    vm.userActivity()
    // Lip sync check: Left / Right move the sound, anything that means "done" closes it; nothing else applies.
    if (vm.isLipSyncCheck) {
        when (code) {
            AKey.KEYCODE_DPAD_LEFT -> if (down) vm.audioEarlier()
            AKey.KEYCODE_DPAD_RIGHT -> if (down) vm.audioLater()
            AKey.KEYCODE_BACK, AKey.KEYCODE_MEDIA_STOP, AKey.KEYCODE_DPAD_CENTER, AKey.KEYCODE_ENTER -> if (up) vm.close()
        }
        return true
    }
    if (vm.isStillWatchingVisible) {
        if (code == AKey.KEYCODE_BACK || code == AKey.KEYCODE_MEDIA_STOP) { if (up) vm.stopWatching(); return true }
        if (code == AKey.KEYCODE_MEDIA_PLAY_PAUSE || code == AKey.KEYCODE_MEDIA_PLAY) { if (up) vm.continueWatching(); return true }
        return false // the prompt's buttons have focus until it's answered
    }
    // Scrubbing: OK / Play jump to the preview and carry on, Back cancels (key-up, so the key's other half
    // doesn't also press whatever is focused).
    if (vm.isScrubbing) when (code) {
        AKey.KEYCODE_BACK, AKey.KEYCODE_MEDIA_STOP -> { if (up) vm.cancelScrub(); return true }
        AKey.KEYCODE_DPAD_CENTER, AKey.KEYCODE_ENTER, AKey.KEYCODE_NUMPAD_ENTER, AKey.KEYCODE_SPACE,
        AKey.KEYCODE_MEDIA_PLAY_PAUSE, AKey.KEYCODE_MEDIA_PLAY -> { if (up) vm.commitScrub(); return true }
    }
    // A seek in progress owns Left / Right until it's committed, wherever focus went in between (the OSD
    // appearing mid-press moves focus; the key-up used to land elsewhere and leave the seek stuck).
    if (vm.pendingSeekMs != null && (code == AKey.KEYCODE_DPAD_LEFT || code == AKey.KEYCODE_DPAD_RIGHT)) {
        if (down) vm.holdSeek(code == AKey.KEYCODE_DPAD_RIGHT, e.nativeKeyEvent.repeatCount) else if (up) vm.scrubKeyUp()
        return true
    }
    val panelOpen = vm.isTracksOpen || vm.isSubtitleSearchOpen
    when (code) {
        AKey.KEYCODE_BACK, AKey.KEYCODE_MEDIA_STOP -> {
            if (!up) return true
            when {
                vm.isSubtitleSearchOpen -> vm.closeSubtitleSearch()
                vm.isTracksOpen -> vm.isTracksOpen = false
                else -> vm.close()
            }
            return true
        }
        AKey.KEYCODE_MEDIA_PLAY_PAUSE, AKey.KEYCODE_SPACE -> { if (down) vm.togglePlay(); return true }
        AKey.KEYCODE_MEDIA_PLAY -> { if (down && !vm.isPlaying) vm.togglePlay(); return true }
        AKey.KEYCODE_MEDIA_PAUSE -> { if (down && vm.isPlaying) vm.togglePlay(); return true }
        AKey.KEYCODE_MEDIA_FAST_FORWARD -> { if (down) vm.seekBy(AppServices.settings.current.playback.seekStepSeconds * 3); return true }
        AKey.KEYCODE_MEDIA_REWIND -> { if (down) vm.seekBy(-AppServices.settings.current.playback.seekStepSeconds * 3); return true }
        AKey.KEYCODE_MEDIA_NEXT -> { if (down) { if (vm.isLive) vm.channelUp() else if (vm.hasNext) vm.playNext() }; return true }
        AKey.KEYCODE_MEDIA_PREVIOUS -> { if (down) { if (vm.isLive) vm.channelDown() else vm.playPrevious() }; return true }
        AKey.KEYCODE_CHANNEL_UP -> { if (down) { if (vm.isLive) vm.channelUp() else vm.nextChapter() }; return true }
        AKey.KEYCODE_CHANNEL_DOWN -> { if (down) { if (vm.isLive) vm.channelDown() else vm.previousChapter() }; return true }
        AKey.KEYCODE_PAGE_UP -> { if (down) { if (vm.isLive) vm.channelUp() else vm.previousChapter() }; return true }
        AKey.KEYCODE_PAGE_DOWN -> { if (down) { if (vm.isLive) vm.channelDown() else vm.nextChapter() }; return true }
        AKey.KEYCODE_INFO, AKey.KEYCODE_I -> { if (down) vm.showBadgesNow(); return true }
        AKey.KEYCODE_CAPTIONS -> { if (down) vm.toggleTracks(); return true }
        AKey.KEYCODE_M -> { if (down) vm.toggleMute(); return true }
        AKey.KEYCODE_S -> { if (down && vm.isSkipVisible) vm.skip(); return true }
        AKey.KEYCODE_N -> { if (down && vm.hasNext) vm.playNext(); return true }
        AKey.KEYCODE_MEDIA_AUDIO_TRACK -> { if (down) vm.cycleTrack(audio = true); return true }
        AKey.KEYCODE_LAST_CHANNEL -> { if (down && vm.isLive) vm.lastChannel(); return true }
        // Guide / TV key: from a channel, straight to the Live TV guide.
        AKey.KEYCODE_GUIDE, AKey.KEYCODE_TV -> {
            if (down && vm.isLive) { vm.close(); Main.shell?.navigateRoot(io.github.devall96.nettle.tv.vm.LiveTvModel()) }
            return vm.isLive
        }
    }
    // Number keys: a channel number on Live TV (tunes after a pause or on OK), 0–9 = 0–90 % into a video.
    val digit = when (code) {
        in AKey.KEYCODE_0..AKey.KEYCODE_9 -> code - AKey.KEYCODE_0
        in AKey.KEYCODE_NUMPAD_0..AKey.KEYCODE_NUMPAD_9 -> code - AKey.KEYCODE_NUMPAD_0
        else -> null
    }
    if (digit != null && !panelOpen) {
        if (down) { if (vm.isLive) vm.typeChannelDigit(digit) else vm.seekToPercent(digit * 10) }
        return true
    }
    if (vm.isLive && vm.typedChannel != null && (code == AKey.KEYCODE_DPAD_CENTER || code == AKey.KEYCODE_ENTER)) {
        if (up) vm.commitTypedChannel()
        return true
    }
    if (panelOpen) { vm.showOsd(); return false }
    if (!vm.isOsdVisible) {
        // OSD hidden: Left / Right seek (hold to go faster), Up / Down change channel on Live TV, OK shows the controls.
        when (code) {
            AKey.KEYCODE_DPAD_LEFT, AKey.KEYCODE_DPAD_RIGHT -> {
                if (vm.isLive) { if (up) vm.showOsd(); return true }
                if (down) vm.holdSeek(code == AKey.KEYCODE_DPAD_RIGHT, e.nativeKeyEvent.repeatCount) else if (up) vm.scrubKeyUp()
                return true
            }
            AKey.KEYCODE_DPAD_UP, AKey.KEYCODE_DPAD_DOWN -> {
                if (vm.isLive) { if (down) { if (code == AKey.KEYCODE_DPAD_UP) vm.channelUp() else vm.channelDown() } }
                else if (up) vm.showOsd()
                return true
            }
            AKey.KEYCODE_DPAD_CENTER, AKey.KEYCODE_ENTER -> { if (up) vm.showOsd(); return true }
        }
        return false
    }
    vm.showOsd()
    return false
}

@Composable
private fun OsdButton(icon: ImageVector, onClick: () -> Unit, big: Boolean = false, focusRequester: FocusRequester? = null, enabled: Boolean = true) {
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    Box(
        Modifier.clip(CircleShape).background(if (focused) Color(0x33FFFFFF) else Color.Transparent)
            .border(2.dp, if (focused) Nettle.colors.accent else Color.Transparent, CircleShape)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick).padding(12.dp),
    ) { Icon(icon, size = if (big) 40.dp else 26.dp, tint = White) }
}

@Composable
private fun SourcePill(text: String, green: Boolean) {
    Box(Modifier.clip(RoundedCornerShape(4.dp)).background(if (green) Color(0x3322C55E) else Color(0x33FFFFFF)).padding(7.dp, 1.dp)) {
        T(text, Modifier.alpha(if (green) 1f else 0.8f), size = 11.sp, weight = FontWeight.Bold, color = if (green) Color(0xFF86EFAC) else White)
    }
}

@Composable
private fun VodControls(vm: PlayerModel, seekBar: FocusRequester, playPause: FocusRequester, modifier: Modifier) {
    Column(modifier.fillMaxWidth().padding(start = 44.dp, end = 44.dp, bottom = 30.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            T(vm.positionText, Modifier.padding(end = 14.dp), weight = FontWeight.SemiBold, color = White)
            SeekBar(vm, seekBar, Modifier.weight(1f))
            T(vm.remainingText, Modifier.padding(start = 14.dp), weight = FontWeight.SemiBold, color = White)
        }
        Box(Modifier.fillMaxWidth()) {
            Row(Modifier.align(Alignment.CenterStart), horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
                // No volume control: the remote sets the TV's (or receiver's) own volume, and a second one in the
                // app only made things quieter by surprise.
                vm.endsAtText?.let { T(it, Modifier.padding(start = 16.dp).alpha(0.7f), color = White) }
            }
            Row(Modifier.align(Alignment.Center), horizontalArrangement = Arrangement.spacedBy(18.dp), verticalAlignment = Alignment.CenterVertically) {
                val step = AppServices.settings.current.playback.seekStepSeconds
                OsdButton(Icons.Rewind, { vm.seekBy(-step) })
                OsdButton(if (vm.isPlaying) Icons.Pause else Icons.Play, { vm.togglePlay() }, big = true, focusRequester = playPause)
                OsdButton(Icons.Forward, { vm.seekBy(step) })
            }
            if (vm.hasNext) Row(Modifier.align(Alignment.CenterEnd)) { OsdButton(Icons.SkipNext, { vm.playNext() }) }
        }
    }
}

/** The seek bar (Fluent slider look) with chapter ticks and the trickplay preview; Left / Right seek, hold to go faster. */
@Composable
private fun SeekBar(vm: PlayerModel, requester: FocusRequester, modifier: Modifier) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    var width by remember { mutableStateOf(0) }
    val len = vm.lengthMs.coerceAtLeast(1)
    val frac = if (vm.lengthMs <= 1) 0f else (vm.positionMs.toFloat() / len).coerceIn(0f, 1f)
    Box(
        modifier.height(32.dp).onSizeChanged { width = it.width }
            .focusRequester(requester).focusable(interactionSource = source)
            .onPreviewKeyEvent { e ->
                val code = e.nativeKeyEvent.keyCode
                if (code != AKey.KEYCODE_DPAD_LEFT && code != AKey.KEYCODE_DPAD_RIGHT) return@onPreviewKeyEvent false
                if (e.type == KeyEventType.KeyDown) vm.holdSeek(code == AKey.KEYCODE_DPAD_RIGHT, e.nativeKeyEvent.repeatCount)
                else if (e.type == KeyEventType.KeyUp) vm.scrubKeyUp()
                true
            },
        contentAlignment = Alignment.CenterStart,
    ) {
        val accent = p.accent
        val marks = vm.chapterMarks
        Canvas(Modifier.fillMaxWidth().height(32.dp)) {
            val cy = size.height / 2
            val th = 4.dp.toPx()
            drawRoundRect(Color(0x66FFFFFF), Offset(0f, cy - th / 2), Size(size.width, th), CornerRadius(th / 2))
            drawRoundRect(accent, Offset(0f, cy - th / 2), Size(size.width * frac, th), CornerRadius(th / 2))
            marks.forEach { m -> drawRect(Color(0xCCFFFFFF), Offset(size.width * m - 1.dp.toPx(), cy - 6.dp.toPx()), Size(2.dp.toPx(), 12.dp.toPx())) }
            val r = (if (focused) 11.dp else 9.dp).toPx()
            drawCircle(Color.White, r, Offset(size.width * frac, cy))
            drawCircle(accent, r * 0.55f, Offset(size.width * frac, cy))
            if (focused) drawCircle(accent, r + 3.dp.toPx(), Offset(size.width * frac, cy), style = androidx.compose.ui.graphics.drawscope.Stroke(2.dp.toPx()))
        }
        // Scrubbing preview over the target position.
        if (vm.pendingSeekMs != null && vm.trickTime.isNotEmpty()) {
            val d = LocalDensity.current
            val boxW = with(d) { 288.dp.toPx() }
            val x = (width * frac - boxW / 2).coerceIn(0f, (width - boxW).coerceAtLeast(0f))
            Column(
                Modifier.offset { IntOffset(x.toInt(), -with(d) { (if (vm.trickImage != null) 220.dp else 70.dp).toPx() }.toInt()) }
                    .width(288.dp).clip(RoundedCornerShape(10.dp)).background(Color(0xF0101010)).border(1.dp, Color(0x40FFFFFF), RoundedCornerShape(10.dp)).padding(4.dp),
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                val bmp = vm.trickImage
                val rect = vm.trickRect
                if (bmp != null && rect != null) {
                    val img = remember(bmp) { bmp.asImageBitmap() }
                    Canvas(Modifier.width(280.dp).aspectRatio(vm.trickAspect).clip(RoundedCornerShape(7.dp))) {
                        drawImage(img, IntOffset(rect.left, rect.top), IntSize(rect.width(), rect.height()), dstSize = IntSize(size.width.toInt(), size.height.toInt()))
                    }
                }
                T(vm.trickTime, Modifier.padding(start = 6.dp, end = 6.dp, bottom = 2.dp), weight = FontWeight.Bold, color = White, maxLines = 1)
            }
        }
    }
}

@Composable
private fun LiveControls(vm: PlayerModel, playPause: FocusRequester, modifier: Modifier) {
    Row(modifier.fillMaxWidth().padding(start = 44.dp, end = 44.dp, bottom = 36.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.padding(end = 20.dp).size(120.dp, 72.dp).clip(RoundedCornerShape(10.dp)).background(Color(0x33FFFFFF)).padding(8.dp)) {
            NetImage(vm.logoUrl, Modifier.fillMaxSize(), contentScale = androidx.compose.ui.layout.ContentScale.Fit)
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.clip(RoundedCornerShape(4.dp)).background(Color(0xFFE53935)).padding(6.dp, 1.dp)) { T("LIVE", size = 11.sp, weight = FontWeight.Bold, color = White) }
                vm.channelNumber?.let { T(it, weight = FontWeight.Bold, color = White) }
                T(vm.title, weight = FontWeight.Bold, color = White, maxLines = 1)
                vm.liveSourceLabel?.let { SourcePill(it, vm.isLiveDirect) }
            }
            T(vm.nowTitle ?: "", size = 22.sp, weight = FontWeight.Bold, color = White, maxLines = 1)
            vm.subtitle?.takeIf { it.isNotEmpty() }?.let { T(it, Modifier.alpha(0.8f), color = White, maxLines = 1) }
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                vm.nowTime?.let { T(it, Modifier.alpha(0.8f), color = White) }
                ThinProgress(vm.liveProgress, Modifier.width(220.dp))
            }
            vm.nextTitle?.let { T(it, Modifier.alpha(0.65f), color = White, maxLines = 1) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            OsdButton(Icons.ChevronLeft, { vm.channelDown() })
            OsdButton(if (vm.isPlaying) Icons.Pause else Icons.Play, { vm.togglePlay() }, big = true, focusRequester = playPause)
            OsdButton(Icons.ChevronRight, { vm.channelUp() })
        }
    }
}

@Composable
private fun UpNext(vm: PlayerModel, modifier: Modifier) {
    val play = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { play.requestFocus() } }
    Row(modifier.width(420.dp).clip(RoundedCornerShape(16.dp)).background(Color(0xE6101010)).padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(150.dp, 84.dp).clip(RoundedCornerShape(10.dp)).background(Color(0x33FFFFFF))) { NetImage(vm.upNextImage, Modifier.fillMaxSize()) }
        Column(Modifier.padding(start = 14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            T("UP NEXT", Modifier.alpha(0.7f), size = 11.sp, weight = FontWeight.Bold, color = White, style = Nettle.type.body.copy(letterSpacing = 1.2.sp))
            T(vm.upNextTitle ?: "", weight = FontWeight.SemiBold, color = White, maxLines = 2)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                NButton({ vm.playNext() }, kind = ButtonKind.Primary, padding = PaddingValues(14.dp, 6.dp), focusRequester = play) { BT("Play in ${vm.upNextCountdown}") }
                TextButton("Hide", { vm.dismissUpNext() }, padding = PaddingValues(14.dp, 6.dp))
            }
        }
    }
}

@Composable
private fun Badge(b: FormatBadge) {
    val p = Nettle.colors
    Column(
        Modifier.alpha(if (b.verified) 1f else 0.8f).widthIn(max = 420.dp).clip(RoundedCornerShape(12.dp)).background(Color(0xD90A0A0F))
            .border(1.5.dp, if (b.verified) p.accent else Color(0x66FFFFFF), RoundedCornerShape(12.dp)).padding(16.dp, 10.dp),
        horizontalAlignment = Alignment.Start, verticalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        T(b.label, size = 20.sp, weight = FontWeight.ExtraBold, color = White, style = Nettle.type.body.copy(letterSpacing = 1.5.sp))
        T(b.detail, Modifier.alpha(0.75f), size = 12.sp, color = White)
    }
}

@Composable
private fun PanelLabel(text: String, top: Boolean = false) =
    T(text, Modifier.padding(start = 4.dp, top = if (top) 0.dp else 12.dp, bottom = 4.dp).alpha(0.6f), size = 12.sp, weight = FontWeight.Bold,
        color = White, style = Nettle.type.body.copy(letterSpacing = 1.2.sp))

/** Button.track: full-width, left-aligned, AccentSoft when selected, Accent frame when focused. */
@Composable
private fun TrackButton(text: String, selected: Boolean, onClick: () -> Unit, focusRequester: FocusRequester? = null, detail: String? = null,
                        dim: Boolean = false, enabled: Boolean = true) {
    val p = Nettle.colors
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val shape = RoundedCornerShape(12.dp)
    Column(
        Modifier.fillMaxWidth().alpha(if (dim) 0.85f else 1f).clip(shape).background(if (selected) p.accentSoft else Color.Transparent)
            .border(2.dp, if (focused) p.accent else Color.Transparent, shape)
            .then(if (focusRequester != null) Modifier.focusRequester(focusRequester) else Modifier)
            .clickable(source, indication = null, enabled = enabled, onClick = onClick).padding(12.dp, 8.dp),
    ) {
        T(text, weight = FontWeight.SemiBold, color = White, maxLines = 1)
        detail?.let { T(it, Modifier.alpha(0.6f), size = 11.5.sp, color = White, maxLines = 1) }
    }
}

@Composable
private fun TuneRow(label: String, value: String, minus: () -> Unit, plus: () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        T(label, Modifier.weight(1f), color = White)
        TextButton("−", minus, padding = PaddingValues(10.dp, 2.dp))
        T(value, Modifier.widthIn(min = 64.dp), color = White, align = TextAlign.Center)
        TextButton("+", plus, padding = PaddingValues(10.dp, 2.dp))
    }
}

@Composable
private fun TracksPanel(vm: PlayerModel, first: FocusRequester, modifier: Modifier) {
    LazyColumn(
        modifier.width(360.dp).heightIn(max = 560.dp).clip(RoundedCornerShape(16.dp)).background(Color(0xF0141414)).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        item { PanelLabel("AUDIO", top = true) }
        items(vm.audioTracks, key = { "a" + it.id }) { t -> TrackButton(t.name, t.selected, { vm.selectTrack(t) }, if (t === vm.audioTracks.first()) first else null) }
        if (vm.audioTracks.isEmpty()) item { T("No audio tracks", Modifier.padding(start = 4.dp).alpha(0.6f), color = White) }
        if (vm.audioTracks.isNotEmpty()) item { TuneRow("Audio timing · ${vm.refreshKey} Hz", vm.audioOffsetText, { vm.audioEarlier() }, { vm.audioLater() }) }
        item { PanelLabel("SUBTITLES") }
        items(vm.subtitleTracks, key = { "s" + it.id }) { t ->
            TrackButton(t.name, t.selected, { vm.selectTrack(t) }, if (vm.audioTracks.isEmpty() && t === vm.subtitleTracks.first()) first else null)
        }
        if (vm.isVod) {
            item { TrackButton("Find subtitles online…", false, { vm.findSubtitles() }, dim = true) }
            item { PanelLabel("PLAYBACK") }
            item { TuneRow("Speed", vm.speedText, { vm.slower() }, { vm.faster() }) }
            item { TuneRow("Subtitle size", vm.subtitleScaleText, { vm.smallerSubtitles() }, { vm.largerSubtitles() }) }
            item { TrackButton("Reset to normal", false, { vm.resetTuning() }, dim = true) }
        }
    }
}

@Composable
private fun SubtitleSearchPanel(vm: PlayerModel, first: FocusRequester, modifier: Modifier) {
    Column(modifier.width(460.dp).heightIn(max = 600.dp).clip(RoundedCornerShape(16.dp)).background(Color(0xF8141414)).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            PanelLabel("FIND SUBTITLES", top = true)
            Spacer(Modifier.weight(1f))
            OsdButton(Icons.Close, { vm.closeSubtitleSearch() })
        }
        Column(Modifier.padding(4.dp, 8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                TvTextField(vm.subtitleSearchLanguage, { vm.subtitleSearchLanguage = it }, Modifier.width(90.dp), placeholder = "eng", focusRequester = first,
                    onSubmit = { vm.findSubtitles() })
                TextButton("Search", { vm.findSubtitles() })
                if (vm.isSubtitleSearching) Spinner(size = 22.dp)
            }
            vm.subtitleSearchStatus?.let { T(it, Modifier.alpha(0.8f), color = White) }
        }
        LazyColumn(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            items(vm.remoteSubtitles, key = { it.info.id ?: it.name }) { s ->
                TrackButton(s.name, false, { vm.downloadSubtitle(s) }, detail = s.detail, enabled = !s.isDownloading)
            }
        }
    }
}

/**
 * Live TV's tuning screen (like a cable box's): the theme background with the channel's logo,
 * number, name and what's on, while a channel or program switch has no picture yet. Hides the
 * switch itself, so letterboxed channels never show the old frame stretched in between.
 */
@Composable
private fun TuningScreen(vm: PlayerModel) {
    val p = Nettle.colors
    val visible = vm.isTuningScreenVisible
    val alpha by animateFloatAsState(if (visible) 1f else 0f, tween(if (visible || !Nettle.motion) 0 else 220), label = "tuning")
    if (alpha <= 0f) return
    val bg = remember(p) { Modifier.fillMaxSize().cachedGradients(p.backgroundWithGlow) }
    Box(bg.graphicsLayer { this.alpha = alpha }, contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Box(Modifier.size(260.dp, 156.dp).background(p.surface, RoundedCornerShape(18.dp)).padding(22.dp), contentAlignment = Alignment.Center) {
                if (vm.logoUrl == null) T(vm.title.take(2).uppercase(), size = 44.sp, weight = FontWeight.Bold, muted = true)
                NetImage(vm.logoUrl, Modifier.fillMaxSize(), contentScale = androidx.compose.ui.layout.ContentScale.Fit)
            }
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                vm.channelNumber?.let { T(it, style = Nettle.type.h2, color = p.accent) }
                T(vm.title, style = Nettle.type.h2, maxLines = 1)
            }
            vm.nowTitle?.takeIf { it.isNotEmpty() }?.let { T(it, style = Nettle.type.h3, muted = true, maxLines = 1) }
            Row(Modifier.padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.CenterVertically) {
                Spinner(size = 22.dp)
                T("Tuning…", muted = true, weight = FontWeight.SemiBold)
            }
        }
    }
}
