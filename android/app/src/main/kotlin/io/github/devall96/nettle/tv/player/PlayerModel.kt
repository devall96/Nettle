package io.github.devall96.nettle.tv.player

import android.graphics.Bitmap
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import coil3.request.ImageRequest
import coil3.request.SuccessResult
import coil3.request.allowHardware
import coil3.toBitmap
import io.github.devall96.nettle.core.api.ApiException
import io.github.devall96.nettle.core.models.*
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.core.services.PlaybackSegment
import io.github.devall96.nettle.core.services.SegmentKind
import io.github.devall96.nettle.core.services.SegmentService
import io.github.devall96.nettle.tv.AppServices
import io.github.devall96.nettle.tv.vm.Format
import io.github.devall96.nettle.tv.vm.Img
import io.github.devall96.nettle.tv.vm.Main
import kotlinx.coroutines.*
import io.github.devall96.nettle.core.api.await
import okhttp3.Request
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

data class FormatBadge(val label: String, val detail: String, val verified: Boolean)
data class TrackOption(val id: Int, val name: String, val audio: Boolean, val streamIndex: Int?, val selected: Boolean)
data class Chapter(val ms: Long, val title: String?)
class RemoteSub(val info: RemoteSubtitleInfo) {
    val name = info.name ?: "Subtitle"
    val detail = listOfNotNull(info.providerName, info.format?.uppercase(), info.threeLetterISOLanguageName,
        info.downloadCount?.let { "%,d downloads".format(it) }, if (info.isHashMatch == true) "exact match" else null,
        if (info.hearingImpaired == true) "SDH" else null, if (info.forced == true) "forced" else null).joinToString("  ·  ")
    var isDownloading by mutableStateOf(false)
}

/** Port of PlayerViewModel (+ Extras): one player screen for library items, queues and Live TV. */
class PlayerModel private constructor() {
    private val settings = AppServices.settings.current.playback
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    val engine = Engine(AppServices.context, settings.audioPassthrough)
    val stopped = CompletableDeferred<Unit>()
    private var disposed = false
    private var currentUrl: String? = null

    // Library playback state
    private var item: BaseItemDto? = null
    private var mediaSourceId: String? = null
    private var playSessionId: String? = null
    private var segments: List<PlaybackSegment> = emptyList()
    private val autoSkipped = HashSet<PlaybackSegment>()
    private var dismissedSegment: PlaybackSegment? = null
    private var nextEpisode: BaseItemDto? = null
    private var upNextDismissed = false
    private var lastReport = 0L
    private var lastInput = System.currentTimeMillis()
    private var upNextShownAt = 0L
    private var queue: MutableList<BaseItemDto>? = null
    private var queueIndex = 0
    private var externalSubs = ArrayList<ExternalSub>()
    private var sourceStreams: List<MediaStream>? = null
    private var mediaGeneration = 0
    private var lastPositionMs = 0L
    private var audioStreamIndex: Int? = null
    private var subtitleStreamIndex: Int? = null
    private var reportAfter: Long? = null
    private var sourcePending = false

    // Live state
    private var channels: List<NtvChannel>? = null
    private var channelIndex = 0
    private var bannerUntil = 0L
    private var liveSchedule: List<NtvSlot> = emptyList()
    private var liveSlot: NtvSlot? = null
    private var tuneVersion = 0
    private var switchingSlot = false
    private var livePausedAt: Long? = null
    private var feedWatchStart: Long? = null
    private var liveRetryVersion = -1

    // ---- what the screen shows ----
    var title by mutableStateOf("")
    var sourceLabel by mutableStateOf<String?>(null)
    var subtitle by mutableStateOf<String?>(null)
    var logoUrl by mutableStateOf<String?>(null)
    var isPlaying by mutableStateOf(false)
    var isBuffering by mutableStateOf(true)
    var isOsdVisible by mutableStateOf(true)
    var lengthMs by mutableStateOf(1L)
    var positionMs by mutableStateOf(0L)
    var positionText by mutableStateOf("0:00")
    var remainingText by mutableStateOf("")
    var endsAtText by mutableStateOf<String?>(null)
    var volume by mutableStateOf(settings.volume.coerceIn(0, 100))
    var isMuted by mutableStateOf(false)
    var isTracksOpen by mutableStateOf(false)
    var skipLabel by mutableStateOf<String?>(null)
    var isSkipVisible by mutableStateOf(false)
    var isUpNextVisible by mutableStateOf(false)
    var upNextTitle by mutableStateOf<String?>(null)
    var upNextImage by mutableStateOf<String?>(null)
    var upNextCountdown by mutableStateOf(0)
    var hasNext by mutableStateOf(false)
    var isLive by mutableStateOf(false)
    var nowTitle by mutableStateOf<String?>(null)
    var nowTime by mutableStateOf<String?>(null)
    var nextTitle by mutableStateOf<String?>(null)
    var liveProgress by mutableStateOf(0f)
    var isChannelBannerVisible by mutableStateOf(false)
    var channelNumber by mutableStateOf<String?>(null)
    var liveSourceLabel by mutableStateOf<String?>(null)
    var isLiveDirect by mutableStateOf(false)
    var liveInterstitial by mutableStateOf<String?>(null)
    /** A channel change is under way (until the new stream starts loading; then until its first frame). */
    var tuning by mutableStateOf(false); private set
    /** Live TV shows its tuning screen while a channel or program switch has no picture yet. */
    val isTuningScreenVisible: Boolean get() = isLive && error == null && (tuning || !engine.firstFrameShown)
    var error by mutableStateOf<String?>(null)
    val audioTracks = mutableStateListOf<TrackOption>()
    val subtitleTracks = mutableStateListOf<TrackOption>()
    val formatBadges = mutableStateListOf<FormatBadge>()
    var isFormatBadgesVisible by mutableStateOf(false)
    var isStillWatchingVisible by mutableStateOf(false)
    var stillWatchingDetail by mutableStateOf<String?>(null)
    var osdMessage by mutableStateOf<String?>(null)
    var chapterMarks by mutableStateOf<List<Float>>(emptyList())
    private var chapters: List<Chapter> = emptyList()
    var playbackSpeed by mutableStateOf(settings.playbackSpeed.coerceIn(0.25, 3.0))
    var subtitleScale by mutableStateOf(settings.subtitleScale.coerceIn(0.5, 2.5))
    // Hold-to-seek: where the seek will land when the key is released (null = not seeking).
    var pendingSeekMs by mutableStateOf<Long?>(null)
    // Trickplay preview
    var trickImage by mutableStateOf<Bitmap?>(null)
    var trickRect by mutableStateOf<android.graphics.Rect?>(null)
    var trickTime by mutableStateOf("")
    var trickAspect by mutableStateOf(16f / 9f)
    // Subtitles found by the server
    var isSubtitleSearchOpen by mutableStateOf(false)
    var isSubtitleSearching by mutableStateOf(false)
    var subtitleSearchStatus by mutableStateOf<String?>(null)
    var subtitleSearchLanguage by mutableStateOf("")
    val remoteSubtitles = mutableStateListOf<RemoteSub>()

    val isVod get() = !isLive
    val nextTip get() = if (queue != null) "Next" else "Next episode"
    val speedText get() = "%.2f".format(Locale.ROOT, playbackSpeed).trimEnd('0').trimEnd('.') + "×"
    val subtitleScaleText get() = "${(subtitleScale * 100).toInt()}%"

    private val audioLang get() = AppServices.session.preferredAudioLanguage(settings.preferredAudioLanguage)
    private val subLang get() = AppServices.session.preferredSubtitleLanguage(settings.preferredSubtitleLanguage)
    private val bufferSeconds get() = settings.networkBufferSeconds.coerceIn(2, 300)

    /** Live TV asks after this long with no input at all. */
    private val liveStillWatchingAfterMs = 4 * 3600_000L
    private var autoPlayedInARow = 0
    private var lastUserInput = System.currentTimeMillis()

    init {
        engine.setVolume(volume)
        if (kotlin.math.abs(playbackSpeed - 1) > 0.001) engine.setSpeed(playbackSpeed)
        engine.onEnded = { onEnded() }
        engine.onError = { reason -> val t = tuneVersion; scope.launch { onPlaybackError(t, reason) } }
        engine.onReady = {
            Log.info("playing: ${engine.describeOutput()}")
            isBuffering = false
            // Some files don't tell the decoder their frame rate: use what Jellyfin measured for refresh-rate matching.
            if (engine.frameRate <= 0f) sourceStreams?.firstOrNull { it.type == "Video" }?.let { v ->
                (v.realFrameRate ?: v.averageFrameRate)?.takeIf { it > 1f }?.let { fps -> engine.onFrameRate?.invoke(fps) }
            }
            loadTracks()
            scope.launch { showFormatBadges(tuneVersion, mediaGeneration) }
            if (engine.audioUnsupported) flash("This audio can't be decoded on this TV. Turn on audio passthrough in Settings, or pick another track.", 6000)
        }
        scope.launch { while (isActive) { onTick(); delay(250) } }
    }

    companion object {
        fun forItem(item: BaseItemDto, startTicks: Long?, mediaSourceId: String?): PlayerModel =
            PlayerModel().apply { scope.launch { startItem(item, startTicks, mediaSourceId) } }

        /** Plays a list in order (Play all, Shuffle, or a remote control's queue), from [index]. */
        fun forQueue(items: List<BaseItemDto>, index: Int, startTicks: Long?): PlayerModel = PlayerModel().apply {
            queue = items.toMutableList()
            queueIndex = index.coerceIn(0, items.size - 1)
            scope.launch { startItem(queue!![queueIndex], startTicks, null) }
        }

        fun forLive(channels: List<NtvChannel>, index: Int): PlayerModel = PlayerModel().apply {
            isLive = true
            this.channels = channels
            channels.forEach { AppServices.allowHostOf(it.streamUrl) }
            tuneTo(index)
        }

        private val clock = DateTimeFormatter.ofPattern("h:mm", Locale.US)
        private val clockAmPm = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
        fun local(ms: Long): LocalDateTime = LocalDateTime.ofInstant(Instant.ofEpochMilli(ms), ZoneId.systemDefault())
    }

    // ===================== Play queue =====================

    /** Adds items right after the current one ("play next") or at the end (remote control). */
    fun enqueue(items: List<BaseItemDto>, next: Boolean) {
        if (items.isEmpty()) return
        val q = queue ?: (item?.let { mutableListOf(it) } ?: mutableListOf()).also { queue = it; queueIndex = 0 }
        q.addAll(if (next) minOf(queueIndex + 1, q.size) else q.size, items)
        setNextFromQueue()
    }

    private fun setNextFromQueue() {
        val q = queue ?: return
        nextEpisode = if (queueIndex + 1 < q.size) q[queueIndex + 1] else null
        nextEpisode?.let { n ->
            upNextTitle = if (n.isEpisode) "${n.seriesName}  S${n.parentIndexNumber}:E${n.indexNumber}  ${n.name}" else n.name
            upNextImage = if (n.isEpisode && n.tag("Primary") != null) Img.of(n.id, "Primary", n.tag("Primary"), 420)
            else n.backdropImageTags?.firstOrNull()?.let { Img.of(n.id, "Backdrop", it, 420) } ?: n.tag("Primary")?.let { Img.of(n.id, "Primary", it, 420) }
        }
        hasNext = nextEpisode != null
    }

    /** Like a CD player: early in an item goes back one, otherwise restarts this one. */
    fun playPrevious() {
        if (isLive) return
        val q = queue
        if (q != null && queueIndex > 0 && engine.timeMs < 5000) {
            reportStopped()
            queueIndex--
            scope.launch { startItem(q[queueIndex], null, null) }
            return
        }
        seekToMs(0)
    }

    private suspend fun startItem(it: BaseItemDto, startTicks: Long?, sourceId: String?) {
        // Until the new file is handed to the player, no reports (the old file's position would be saved against this item).
        sourcePending = true
        item = it
        segments = emptyList(); autoSkipped.clear(); dismissedSegment = null
        nextEpisode = null; upNextDismissed = false
        isUpNextVisible = false; isSkipVisible = false; isBuffering = true; isSubtitleSearchOpen = false
        error = null
        chapters = emptyList(); chapterMarks = emptyList()
        if (queue != null) setNextFromQueue()
        hasNext = nextEpisode != null

        title = if (it.isEpisode) it.seriesName ?: it.name ?: "" else it.name ?: ""
        subtitle = if (it.isEpisode) "S${it.parentIndexNumber}:E${it.indexNumber}  ${it.name}" else it.productionYear?.toString()
        logoUrl = it.tag("Logo")?.let { t -> Img.of(it.id, "Logo", t, 500) } ?: it.parentLogoImageTag?.let { t -> Img.of(it.parentLogoItemId, "Logo", t, 500) }
        val jf = AppServices.jellyfin
        try {
            // Ask the server for media sources with transcoding explicitly disabled.
            val info = jf.getPlaybackInfo(it.id, startTicks)
            if (item !== it || disposed) return // something else was picked meanwhile
            val source = info.mediaSources.firstOrNull { s -> s.id == sourceId } ?: info.mediaSources.firstOrNull()
            if (source == null) { error = "The server returned no playable media for this item."; isBuffering = false; return }
            mediaSourceId = source.id
            val vs = source.mediaStreams.firstOrNull { s -> s.type == "Video" }
            sourceStreams = source.mediaStreams
            sourceLabel = listOfNotNull(VideoFormat.resolution(vs?.width), vs?.codec?.uppercase(), VideoFormat.hdrLabel(vs?.videoRangeType).first, "Direct play")
                .filter { s -> s.isNotEmpty() }.joinToString(" · ")
            playSessionId = info.playSessionId
            externalSubs = ArrayList(externalSubsOf(it.id, source))
            val startMs = (startTicks ?: 0) / 10_000
            lastPositionMs = startMs
            swapMedia(libraryLoad(it.id, source, info.playSessionId, startMs))
            sourcePending = false
            jf.reportStart(progress(isPaused = false))
            lastReport = System.currentTimeMillis()
            scope.launch { loadTrickplay(it.id, source.id!!) }
            scope.launch { loadChapters(it.id) }
            segments = AppServices.segments.getSegments(it.id)
            if (queue == null && it.isEpisode && settings.autoPlayNextEpisode) findNextEpisode(it)
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            error = e.message
            isBuffering = false
        }
    }

    /**
     * Everything the player needs for one file from the user's Jellyfin server: the original file, its
     * external subtitles, language / subtitle preferences, a read-ahead sized from its bitrate,
     * and the token as a header (never in the URL).
     */
    private fun libraryLoad(itemId: String, source: MediaSourceInfo, playSessionId: String?, startMs: Long): PlayerLoad {
        val jf = AppServices.jellyfin
        sourceStreams = source.mediaStreams
        return PlayerLoad(
            url = jf.directStreamUrl(itemId, source.id, playSessionId),
            startMs = startMs,
            audioLanguage = audioLang,
            subtitleLanguage = subLang,
            subtitleMode = AppServices.session.subtitleMode(settings.subtitleMode),
            externalSubtitles = externalSubsOf(itemId, source),
            bufferSeconds = bufferSeconds,
            authHeader = jf.tokenHeaderValue,
        )
    }

    private fun externalSubsOf(itemId: String, source: MediaSourceInfo): List<ExternalSub> =
        source.mediaStreams.filter { it.type == "Subtitle" && it.isExternal }.map {
            ExternalSub(AppServices.jellyfin.externalSubtitleUrl(itemId, source.id!!, it), it.displayTitle ?: it.title ?: it.language ?: "External", it.language, it.index)
        }

    private suspend fun findNextEpisode(ep: BaseItemDto) {
        val seriesId = ep.seriesId ?: return
        try {
            // Only the neighbours of this episode, not the whole series with its media info.
            val near = AppServices.jellyfin.getAdjacentEpisodes(seriesId, ep.id).items
            val idx = near.indexOfFirst { it.id == ep.id }
            if (idx >= 0 && idx + 1 < near.size && item?.id == ep.id && queue == null) {
                val n = near[idx + 1]
                nextEpisode = n
                upNextTitle = "S${n.parentIndexNumber}:E${n.indexNumber}  ${n.name}"
                upNextImage = n.tag("Primary")?.let { Img.of(n.id, "Primary", it, 420) }
                hasNext = true
            }
        } catch (e: CancellationException) { throw e } catch (_: Exception) { }
    }

    private val canReport get() = item != null && !sourcePending && !isLive

    private fun progress(isPaused: Boolean, eventName: String? = null): PlaybackProgressInfo {
        val t = engine.timeMs
        if (t > 0 && !engine.ended) lastPositionMs = t
        return PlaybackProgressInfo(
            itemId = item?.id, mediaSourceId = mediaSourceId, playSessionId = playSessionId,
            positionTicks = maxOf(0, lastPositionMs) * 10_000, isPaused = isPaused, isMuted = isMuted, volumeLevel = volume,
            playMethod = "DirectPlay", eventName = eventName, audioStreamIndex = audioStreamIndex, subtitleStreamIndex = subtitleStreamIndex,
        )
    }

    /** Sends a progress report about a second from now (coalesces bursts of seeks). */
    private fun reportSoon() { if (!isLive && item != null) reportAfter = System.currentTimeMillis() + 1000 }

    // ===================== Live (NostalgiaTV) =====================

    private fun tuneTo(index: Int) {
        val chs = channels ?: return
        if (chs.isEmpty()) return
        val target = ((index % chs.size) + chs.size) % chs.size
        if (hasTuned && target != channelIndex) previousChannelIndex = channelIndex
        hasTuned = true
        channelIndex = target
        val ch = chs[channelIndex]
        val version = ++tuneVersion
        feedWatchStart = null
        error = null
        liveInterstitial = null
        isBuffering = true
        title = ch.name
        channelNumber = ch.number
        logoUrl = ch.logoUrl
        liveSlot = null
        bannerUntil = System.currentTimeMillis() + 5000
        isChannelBannerVisible = true
        tuning = true
        // No showOsd() here: surfing with Up / Down (controls hidden) shows only the channel banner,
        // so the next press keeps surfing. With the controls up they stay up (handleKey keeps them).
        scope.launch { tune(ch, version) }
    }

    private suspend fun tune(ch: NtvChannel, version: Int) {
        val stableId = ch.stableId
        if (settings.ntvDirectPlay && stableId != null) {
            try {
                val schedule = AppServices.ntv.getChannelSchedule(stableId, 6)
                if (version != tuneVersion) return
                val now = System.currentTimeMillis()
                val slot = schedule.firstOrNull { it.startTimeMs <= now && it.endTimeMs > now }
                if (slot != null && canDirectPlay(slot)) {
                    liveSchedule = schedule
                    playSlot(slot, version)
                    return
                }
            } catch (e: CancellationException) { throw e } catch (e: Exception) {
                // Schedule unavailable: fall back to the broadcast feed below.
                Log.warn("NTV direct tune failed for $stableId, using feed: ${e.message}")
            }
            if (version != tuneVersion) return
        }
        // Fallback: NostalgiaTV's own broadcast feed (channels with no Jellyfin item, or direct play off).
        isLiveDirect = false
        liveSlot = null
        sourceStreams = null // feed: no file metadata, so only verified resolution is badged
        feedWatchStart = System.currentTimeMillis()
        liveSourceLabel = "NostalgiaTV broadcast feed"
        // NostalgiaTV's own feed: no Jellyfin token goes to it.
        swapMedia(PlayerLoad(url = ch.streamUrl, bufferSeconds = maxOf(20, bufferSeconds)))
        updateLiveInfo()
    }

    private fun norm(id: String?) = (id ?: "").replace("-", "").lowercase()

    /** True when the slot's item lives on the Jellyfin server we're signed into. */
    private fun canDirectPlay(slot: NtvSlot): Boolean =
        slot.jellyfinItem()?.let { norm(it.first) == norm(AppServices.session.current?.serverId) } == true

    /** Plays the slot's original file from Jellyfin, joined at the live broadcast position. */
    private suspend fun playSlot(slot: NtvSlot, version: Int) {
        // Resolves NostalgiaTV's item id, repairing it via title / episode lookup if it's stale.
        val (itemId, info) = AppServices.ntvResolver.getPlayback(slot)
        if (version != tuneVersion) return
        // Computed after the lookup so time spent resolving doesn't put us behind live.
        val offsetMs = maxOf(0, System.currentTimeMillis() - slot.startTimeMs) + (slot.playbackStartTimeMs ?: 0)
        val source = info.mediaSources.firstOrNull() ?: throw IllegalStateException("No media source")
        if (source.id == null) throw IllegalStateException("No media source")
        val load = libraryLoad(itemId, source, info.playSessionId, if (offsetMs > 500) offsetMs else 0)
        liveSlot = slot
        isLiveDirect = true
        liveInterstitial = null
        val v = source.mediaStreams.firstOrNull { it.type == "Video" }
        sourceStreams = source.mediaStreams
        val quality = VideoFormat.resolution(v?.width) ?: slot.videoResolution?.let { it + "p" }
        liveSourceLabel = listOfNotNull("Original", quality, v?.codec?.uppercase(), VideoFormat.hdrLabel(v?.videoRangeType).first, "Direct play")
            .filter { it.isNotEmpty() }.joinToString(" · ")
        swapMedia(load)
        updateLiveInfo()
    }

    /** At a slot boundary, roll straight into the next scheduled program. */
    private suspend fun advanceSlot() {
        val cur = liveSlot ?: return
        if (switchingSlot || channels == null) return
        switchingSlot = true
        Log.info("slot rollover after '${cur.displayTitle}'")
        val version = tuneVersion
        try {
            val now = System.currentTimeMillis()
            val next = liveSchedule.firstOrNull { it.startTimeMs <= now + 1000 && it.endTimeMs > now }
                ?: liveSchedule.firstOrNull { it.startTimeMs >= cur.endTimeMs - 1000 }
            if (next == null || next.endTimeMs <= now || !canDirectPlay(next)) { tuneTo(channelIndex); return }
            playSlot(next, version)
        } catch (e: CancellationException) { throw e } catch (e: Exception) {
            Log.warn("rollover failed, re-tuning: ${e.message}")
            if (version == tuneVersion) tuneTo(channelIndex)
        } finally { switchingSlot = false }
    }

    private fun updateLiveInfo() {
        val chs = channels ?: return
        val now = System.currentTimeMillis()
        val slot = liveSlot
        if (isLiveDirect && slot != null) {
            val nextSlot = liveSchedule.firstOrNull { it.startTimeMs >= slot.endTimeMs - 1000 }
            nowTitle = slot.displayTitle
            nowTime = "${local(slot.startTimeMs).format(clock)} – ${local(slot.endTimeMs).format(clockAmPm)}"
            liveProgress = ((now - slot.startTimeMs).toFloat() / maxOf(1, slot.endTimeMs - slot.startTimeMs)).coerceIn(0f, 1f)
            nextTitle = nextSlot?.let { "Next  ${local(it.startTimeMs).format(clockAmPm)}  ${it.displayTitle}" }
            subtitle = slot.displaySubtitle
            return
        }
        val ch = chs[channelIndex]
        val p = ch.programAt(now)
        val next = if (p != null) ch.programAfter(p.start) else ch.programAfter(now)
        nowTitle = p?.title ?: "No listing"
        nowTime = p?.let { "${local(it.start).format(clock)} – ${local(it.stop).format(clockAmPm)}" }
        liveProgress = p?.let { ((now - it.start).toFloat() / maxOf(1, it.durationMs)).coerceIn(0f, 1f) } ?: 0f
        nextTitle = next?.let { "Next  ${local(it.start).format(clockAmPm)}  ${it.title}" }
        subtitle = p?.subTitle ?: p?.episodeNum
    }

    fun channelUp() = tuneTo(channelIndex + 1)
    fun channelDown() = tuneTo(channelIndex - 1)

    // ===================== Common =====================

    private fun swapMedia(load: PlayerLoad) {
        if (disposed) return
        currentUrl = load.url
        mediaGeneration++
        audioTracks.clear(); subtitleTracks.clear()
        isFormatBadgesVisible = false
        formatBadges.clear()
        Log.info("play ${if (isLive) (if (isLiveDirect) "live-direct" else "live-feed") else "vod"}: ${Log.redact(load.url)}" +
            if (load.authHeader != null) " (token sent as a header)" else "")
        engine.load(load)
        tuning = false
    }

    private fun onTick() {
        if (disposed) return
        isPlaying = engine.isPlaying
        if (error == null && liveInterstitial == null) isBuffering = engine.isBuffering
        val now = System.currentTimeMillis()
        if (isOsdVisible && isPlaying && !isTracksOpen && !isSubtitleSearchOpen && pendingSeekMs == null && now - lastInput > 3500) isOsdVisible = false

        if (isLive) {
            if (isChannelBannerVisible && now > bannerUntil) isChannelBannerVisible = false
            checkFeedWatchdog()
            checkLiveStillWatching()
            val slot = liveSlot
            if (isLiveDirect && slot != null && livePausedAt == null && now >= slot.endTimeMs - 250) scope.launch { advanceSlot() }
            updateLiveInfo()
            return
        }

        val len = engine.lengthMs
        val time = engine.timeMs
        if (len > 0) lengthMs = len
        val pos = pendingSeekMs ?: maxOf(0, time)
        positionMs = pos
        positionText = Format.clock(pos)
        if (len > 0) {
            remainingText = "-" + Format.clock(len - pos)
            endsAtText = "Ends " + LocalDateTime.now().plusNanos((len - pos) * 1_000_000).format(clockAmPm)
        }
        updateSegments(maxOf(0, time), len)

        val due = now - lastReport >= 10_000 || (reportAfter?.let { now >= it } == true)
        if (due && canReport && time > 0 && !engine.isBuffering) {
            lastReport = now
            val eventName = if (reportAfter != null) "TimeUpdate" else null
            reportAfter = null
            val p = progress(!isPlaying, eventName)
            AppServices.scope.launch { AppServices.jellyfin.reportProgress(p) }
        }
    }

    private var currentSkip: PlaybackSegment? = null

    private fun updateSegments(posMs: Long, lenMs: Long) {
        var active = segments.firstOrNull { posMs >= it.startMs && posMs < it.endMs - 500 }
        if (active != null) {
            val action = SegmentService.actionFor(active.kind, settings)
            if (action == SegmentAction.AutoSkip && autoSkipped.add(active)) {
                seekToMs(active.endMs)
                Main.toast(if (active.kind == SegmentKind.Intro) "Skipped intro" else "Skipped ${active.kind.name.lowercase()}")
                active = null
            } else if (action != SegmentAction.ShowButton || active == dismissedSegment) active = null
        }
        // Credits with a known next episode turn into the "Up Next" card instead of a plain skip.
        val creditsNow = active?.kind == SegmentKind.Credits ||
            (segments.none { it.kind == SegmentKind.Credits } && lenMs > 0 && lenMs - posMs < 25_000 && posMs > 60_000)
        if (nextEpisode != null && creditsNow && !upNextDismissed) {
            if (!isUpNextVisible) { isUpNextVisible = true; upNextShownAt = System.currentTimeMillis() }
            upNextCountdown = maxOf(0, settings.upNextCountdownSeconds - ((System.currentTimeMillis() - upNextShownAt) / 1000).toInt())
            if (upNextCountdown == 0 && isPlaying && !isStillWatchingVisible) { autoPlayNext(); return }
            active = null
        } else if (isUpNextVisible && !creditsNow) isUpNextVisible = false
        isSkipVisible = active != null
        skipLabel = active?.skipLabel
        currentSkip = active
    }

    fun skip() {
        val s = currentSkip ?: return
        seekToMs(s.endMs)
        dismissedSegment = s
        isSkipVisible = false
    }

    /** Next episode chosen by the user: counts as being here. */
    fun playNext() { autoPlayedInARow = 0; playNextCore() }

    private fun playNextCore() {
        val next = nextEpisode ?: return
        isUpNextVisible = false
        reportStopped()
        val q = queue
        if (q != null && queueIndex + 1 < q.size) queueIndex++
        scope.launch { startItem(next, null, null) }
    }

    fun dismissUpNext() { upNextDismissed = true; isUpNextVisible = false }

    // ===================== "Are you still watching?" =====================

    /** Any key from the user. */
    fun userActivity() {
        lastUserInput = System.currentTimeMillis()
        autoPlayedInARow = 0
    }

    private fun autoPlayNext() {
        if (nextEpisode == null) return
        if (settings.stillWatchingPrompt && autoPlayedInARow >= maxOf(1, settings.stillWatchingEpisodes)) {
            engine.setPause(true)
            isUpNextVisible = false
            stillWatchingDetail = "${autoPlayedInARow + 1} episodes in a row without a break. Nettle paused before the next one."
            isStillWatchingVisible = true
            Log.info("still-watching prompt (episodes)")
            return
        }
        autoPlayedInARow++
        playNextCore()
    }

    private fun checkLiveStillWatching() {
        if (!settings.stillWatchingPrompt || isStillWatchingVisible || !engine.isPlaying) return
        if (System.currentTimeMillis() - lastUserInput < liveStillWatchingAfterMs) return
        engine.setPause(true)
        if (isLiveDirect) livePausedAt = System.currentTimeMillis()
        stillWatchingDetail = "This channel has been on for 4 hours without any input, so Nettle paused it."
        isStillWatchingVisible = true
        Log.info("still-watching prompt (live)")
    }

    fun continueWatching() {
        isStillWatchingVisible = false
        lastUserInput = System.currentTimeMillis()
        autoPlayedInARow = 0
        if (isLive) { livePausedAt = null; tuneTo(channelIndex); return } // back to the live point, like a real channel
        if (nextEpisode != null) playNextCore() else engine.setPause(false)
    }

    fun stopWatching() { isStillWatchingVisible = false; close() }

    /**
     * A broadcast feed can "play" while sending no picture. If no video frame size has appeared
     * after 15 s, say so instead of a black screen.
     */
    private fun checkFeedWatchdog() {
        val started = feedWatchStart ?: return
        if (isLiveDirect) return
        if (engine.videoWidth > 0) { feedWatchStart = null; return }
        if (System.currentTimeMillis() - started < 15_000 || error != null) return
        feedWatchStart = null
        isBuffering = false
        error = "NostalgiaTV's broadcast feed isn't sending any video for this channel right now. NostalgiaTV may not be able to find the scheduled file. Try refreshing its library, or pick another channel."
        Log.warn("feed watchdog: no video after 15s on $title")
    }

    /** Requests the first byte of the stream to learn the HTTP status after a failure. */
    private suspend fun probeStatus(url: String?): Int? {
        if (url == null || !url.startsWith("http", true)) return null
        return try {
            val b = Request.Builder().url(url).header("Range", "bytes=0-0")
            val jf = AppServices.jellyfin
            if (jf.isServerUrl(url)) jf.tokenHeaderValue?.let { b.header("Authorization", it) }
            withTimeout(8000) { withContext(Dispatchers.IO) { AppServices.http.newCall(b.build()).await().use { it.code } } }
        } catch (e: CancellationException) { null } catch (_: Exception) { null }
    }

    /**
     * Errors are only surfaced if playback is genuinely dead a moment later (a fast channel
     * change can abandon a stream mid-open), and a live channel gets one silent retry first.
     */
    private suspend fun onPlaybackError(tuneAtError: Int, reason: String) {
        Log.warn("playback error: $reason")
        delay(1200)
        if (disposed || tuneAtError != tuneVersion) return
        if (engine.isPlaying || (engine.isBuffering && !engine.ended && engine.exo.playerError == null)) {
            Log.info("ignored stale playback error (player is healthy)")
            return
        }
        val status = probeStatus(currentUrl)
        if (disposed || tuneAtError != tuneVersion) return
        val what = if (isLive) "this program" else "this item"
        if (status == 404 || status == 500 || status == 403) {
            isBuffering = false
            error = when (status) {
                404 -> "Your Jellyfin server can't find the file for $what. The drive or folder it lives on may be offline, unmounted or moved (server returned 404)."
                403 -> "Your Jellyfin server refused access to $what (403). Check this user's library permissions."
                else -> "Your Jellyfin server hit an error reading the file for $what (500). The drive may be failing or disconnected."
            }
            Log.warn("playback error shown (HTTP $status): $error")
            return
        }
        if (isLive && liveRetryVersion != tuneVersion) {
            Log.warn("live stream error, retrying once")
            tuneTo(channelIndex)
            liveRetryVersion = tuneVersion
            return
        }
        isBuffering = false
        error = if (!isLive) "Playback failed ($reason). The file may be unreachable from this TV, or use a format this TV can't decode."
        else if (isLiveDirect) "This program couldn't be loaded from your Jellyfin server."
        else "This channel couldn't be played. Check that NostalgiaTV is running."
        Log.warn("playback error shown: $error")
    }

    private fun onEnded() {
        if (isLive) {
            val slot = liveSlot
            if (isLiveDirect && slot != null) {
                // The file finished a little before its slot did: hold until the next program airs.
                val next = liveSchedule.firstOrNull { it.startTimeMs >= slot.endTimeMs - 1000 }
                liveInterstitial = next?.let { "Coming up at ${local(it.startTimeMs).format(clockAmPm)}\n${it.displayTitle}" } ?: "Coming up next…"
                isBuffering = false
                return
            }
            error = "The channel stream ended."
            return
        }
        if (isStillWatchingVisible) return
        // A list the user started always moves on; single episodes follow the setting.
        if (nextEpisode != null && (settings.autoPlayNextEpisode || queue != null) && !upNextDismissed) { autoPlayNext(); return }
        close()
    }

    fun togglePlay() {
        showOsd()
        val it = item
        if (engine.ended && !isLive && it != null) { scope.launch { startItem(it, null, mediaSourceId) }; return }
        val willPause = engine.isPlaying || !engine.isPaused
        if (isLive && isLiveDirect) {
            if (willPause) livePausedAt = System.currentTimeMillis()
            else livePausedAt?.let { at ->
                livePausedAt = null
                // Live TV resumes at the live point, like a real channel.
                if (System.currentTimeMillis() - at > 3000) { tuneTo(channelIndex); return }
            }
        }
        engine.setPause(willPause)
        isPlaying = !willPause
        if (canReport) { val p = progress(willPause); AppServices.scope.launch { AppServices.jellyfin.reportProgress(p) } }
    }

    fun pause() { if (engine.isPlaying) togglePlay() }

    private var lastSeekTargetMs = 0L
    private var lastSeekAt = 0L

    fun seekBy(seconds: Int) {
        if (isLive) return
        showOsd()
        // Presses in quick succession build on the last target rather than the (stale) reported time.
        val from = if (System.currentTimeMillis() - lastSeekAt < 1500) lastSeekTargetMs else maxOf(0, engine.timeMs)
        seekToMs((from + seconds * 1000L).coerceIn(0, maxOf(0, engine.lengthMs - 1000)))
    }

    fun seekToMs(ms: Long) {
        if (isLive) return
        lastSeekTargetMs = ms
        lastSeekAt = System.currentTimeMillis()
        positionMs = ms
        lastPositionMs = ms
        engine.seekTo(ms)
        reportSoon()
    }

    // ---- Hold-to-seek: the longer Left / Right is held, the bigger the steps ----

    /** Key went down (repeat = how many auto-repeats so far). The jump happens on release. */
    fun holdSeek(forward: Boolean, repeat: Int) {
        if (isLive || engine.lengthMs <= 0) return
        showOsd()
        val step = settings.seekStepSeconds * 1000L * when {
            repeat < 6 -> 1
            repeat < 16 -> 3
            repeat < 30 -> 6
            else -> 30
        }
        val base = pendingSeekMs ?: (if (System.currentTimeMillis() - lastSeekAt < 1500) lastSeekTargetMs else maxOf(0, engine.timeMs))
        val target = (base + if (forward) step else -step).coerceIn(0, maxOf(0, engine.lengthMs - 1000))
        pendingSeekMs = target
        showTrickplay(target)
    }

    fun commitHoldSeek() {
        val t = pendingSeekMs ?: return
        pendingSeekMs = null
        hideTrickplay()
        seekToMs(t)
    }

    fun changeVolume(v: Int) {
        volume = v.coerceIn(0, 100)
        engine.setVolume(volume)
        settings.volume = volume
        showOsd()
    }

    fun toggleMute() { isMuted = !isMuted; engine.setMute(isMuted) }

    fun toggleTracks() {
        isTracksOpen = !isTracksOpen
        if (isTracksOpen) loadTracks()
        showOsd()
    }

    private fun streamIndexOf(t: Track): Int? = t.ffIndex ?: t.externalUrl?.let { u -> externalSubs.firstOrNull { it.url == u }?.streamIndex }

    fun loadTracks() {
        val tracks = engine.tracks
        audioTracks.clear(); subtitleTracks.clear()
        tracks.filter { it.type == "audio" }.forEach { audioTracks += TrackOption(it.id, it.name, true, streamIndexOf(it), it.selected) }
        val subs = tracks.filter { it.type == "sub" }
        subtitleTracks += TrackOption(-1, "Off", false, -1, subs.none { it.selected })
        subs.forEach { subtitleTracks += TrackOption(it.id, it.name, false, streamIndexOf(it), it.selected) }
        audioStreamIndex = audioTracks.firstOrNull { it.selected }?.streamIndex
        subtitleStreamIndex = subtitleTracks.firstOrNull { it.selected }?.streamIndex
    }

    fun selectTrack(t: TrackOption) {
        engine.selectTrack(if (t.audio) "audio" else "sub", t.id)
        val list = if (t.audio) audioTracks else subtitleTracks
        for (i in list.indices) list[i] = list[i].copy(selected = list[i].id == t.id)
        if (t.audio) audioStreamIndex = t.streamIndex else subtitleStreamIndex = t.streamIndex
        // Tell the server right away, so resuming (here or elsewhere) keeps this choice.
        if (canReport) { val p = progress(!isPlaying, if (t.audio) "AudioTrackChange" else "SubtitleTrackChange"); AppServices.scope.launch { AppServices.jellyfin.reportProgress(p) } }
    }

    // ---- Remote shortcuts (see handleKey in PlayerScreen) ----

    /** Digits typed on the remote during Live TV, shown on screen; tunes 1.5 s after the last digit (or on OK). */
    var typedChannel by mutableStateOf<String?>(null); private set
    private var typedJob: Job? = null
    private var previousChannelIndex: Int? = null
    private var hasTuned = false

    fun typeChannelDigit(d: Int) {
        if (!isLive) return
        typedChannel = ((typedChannel ?: "") + d).takeLast(5)
        typedJob?.cancel()
        typedJob = scope.launch { delay(1500); commitTypedChannel() }
    }

    /** Tunes to the typed channel number; true if there was one to commit. */
    fun commitTypedChannel(): Boolean {
        val n = typedChannel ?: return false
        typedJob?.cancel()
        typedChannel = null
        val chs = channels ?: return true
        val i = chs.indexOfFirst { it.number == n }.takeIf { it >= 0 }
            ?: chs.indexOfFirst { it.number?.trimStart('0') == n.trimStart('0') }.takeIf { it >= 0 }
        if (i == null) Main.toast("No channel $n") else tuneTo(i)
        return true
    }

    /** Jumps back to the channel watched before this one (the remote's "last channel" key). */
    fun lastChannel() { previousChannelIndex?.let { tuneTo(it) } }

    /** Number keys on a video: 0 = start, 5 = halfway, 9 = 90 %. */
    fun seekToPercent(p: Int) {
        if (isLive || engine.lengthMs <= 0) return
        showOsd()
        seekToMs(engine.lengthMs * p / 100)
    }

    /** Audio / subtitle key: steps to the next track (subtitles include Off). */
    fun cycleTrack(audio: Boolean) {
        loadTracks()
        val list = if (audio) audioTracks else subtitleTracks
        if (list.size < 2) { Main.toast(if (audio) "Only one audio track" else "No subtitles"); return }
        val next = list[(list.indexOfFirst { it.selected } + 1).mod(list.size)]
        selectTrack(next)
        Main.toast((if (audio) "Audio: " else "Subtitles: ") + next.name)
    }

    fun showOsd() {
        lastInput = System.currentTimeMillis()
        if (!isOsdVisible) isOsdVisible = true
    }

    fun hideOsd() { isOsdVisible = false }

    fun close() = Main.closePlayer()

    private fun reportStopped(): Job? {
        if (!canReport) return null
        val p = progress(isPaused = false).copy(eventName = "Stopped")
        return AppServices.scope.launch { AppServices.jellyfin.reportStopped(p) }
    }

    /** The app went to the background (Home button): pause and save the position. */
    fun onAppStopped() {
        if (engine.isPlaying) togglePlay()
        if (canReport) { val p = progress(true); AppServices.scope.launch { AppServices.jellyfin.reportProgress(p) } }
    }

    // ===================== TV-style format badges =====================

    /**
     * Pops up badges for what is ACTUALLY being output, verified at runtime:
     *  • resolution from the frames really being decoded,
     *  • HDR only if the decoded picture is HDR AND this TV reports it accepts that HDR type,
     *  • premium audio only once that audio is really playing (stated as passed through or decoded).
     */
    suspend fun showFormatBadges(version: Int = tuneVersion, generation: Int = mediaGeneration, delayMs: Long = 1500) {
        if (delayMs > 0) delay(delayMs)
        var w = 0
        var h = 0
        for (i in 0 until 6) {
            if (disposed || version != tuneVersion || generation != mediaGeneration) return
            w = engine.videoWidth; h = engine.videoHeight
            if (w > 0) break
            delay(500)
        }
        if (w == 0) return // no picture yet: claim nothing
        val badges = ArrayList<FormatBadge>()
        val video = sourceStreams?.firstOrNull { it.type == "Video" }
        val res = VideoFormat.resolution(w)
        if (res == "4K" || res == "1440p" || res == "1080p") badges += FormatBadge(res, "${w}×$h decoded", true)

        val caps = AppServices.hdrTypes
        val (hdr, _) = VideoFormat.hdrLabel(video?.videoRangeType)
        val signal = engine.hdrSignal
        if (hdr != null || signal != null) {
            val label = (hdr ?: when (signal) { "dovi" -> "Dolby Vision"; "hlg" -> "HLG"; else -> "HDR10" }).uppercase()
            when {
                signal == "dovi" && caps.dolbyVision -> badges += FormatBadge("DOLBY VISION", "Dolby Vision sent to your TV", true)
                signal == "pq" && hdr?.startsWith("Dolby") == true && caps.hdr10 ->
                    badges += FormatBadge("HDR10", "Your TV doesn't take Dolby Vision, so its HDR10 layer is sent instead", true)
                signal == "pq" && caps.hdr10 -> badges += FormatBadge(if (label == "HDR10+" && caps.hdr10Plus) "HDR10+" else "HDR10",
                    if (label == "HDR10+" && caps.hdr10Plus) "HDR10+ sent to your TV" else "HDR10 sent to your TV", true)
                signal == "hlg" && caps.hlg -> badges += FormatBadge("HLG", "HLG sent to your TV", true)
                signal == null && hdr?.startsWith("Dolby") == true -> badges += FormatBadge("DOLBY VISION",
                    "This TV doesn't take Dolby Vision and the file has no HDR10 layer, so colours may look wrong", false)
                else -> badges += FormatBadge(label, "Your TV doesn't report support for this HDR type, so it may be shown in SDR", false)
            }
        }

        // Audio: only once audio is really playing.
        val audio = pickAudioStream()
        val audioLabel = VideoFormat.audioLabel(audio)
        if (engine.audioActive && audioLabel != null) {
            val layout = VideoFormat.channelLayout(audio!!.channels)
            val detail = if (engine.audioBitstream) "Passed through untouched: your TV or receiver decodes it"
            else if (audioLabel == "Dolby Atmos" || audioLabel == "DTS:X") "Decoded by Nettle to $layout (turn on audio passthrough in Settings to send height channels to a receiver)"
            else "Decoded by Nettle · $layout"
            badges += FormatBadge(audioLabel.uppercase(), detail, true)
        }
        if (generation != mediaGeneration || disposed) return
        formatBadges.clear()
        formatBadges.addAll(badges)
        Log.info("format badges: " + if (badges.isEmpty()) "(none)" else badges.joinToString(" | ") { "${it.label} [${it.detail}]" })
        if (badges.isEmpty()) return
        isFormatBadgesVisible = true
        delay(7000)
        if (generation == mediaGeneration) isFormatBadgesVisible = false
    }

    fun showBadgesNow() { scope.launch { showFormatBadges(delayMs = 0) } }

    /** The audio stream most likely selected (the one playing, else preferred language, else default, else first). */
    private fun pickAudioStream(): MediaStream? {
        val audio = sourceStreams?.filter { it.type == "Audio" } ?: return null
        if (audio.isEmpty()) return null
        audioStreamIndex?.let { idx -> audio.firstOrNull { it.index == idx }?.let { return it } }
        val lang = audioLang
        return (if (!lang.isNullOrEmpty()) audio.firstOrNull { it.language.equals(lang, true) } else null)
            ?: audio.firstOrNull { it.isDefault } ?: audio[0]
    }

    // ===================== Chapters (from the server; Media3 doesn't read them) =====================

    private suspend fun loadChapters(itemId: String) {
        val list = runCatching { AppServices.jellyfin.getItem(itemId).chapters }.getOrNull() ?: return
        if (item?.id != itemId) return
        chapters = list.map { Chapter(it.startPositionTicks / 10_000, it.name) }.sortedBy { it.ms }
        updateChapterMarks()
    }

    private fun updateChapterMarks() {
        val len = engine.lengthMs.takeIf { it > 0 } ?: item?.runTimeTicks?.div(10_000) ?: 0
        chapterMarks = if (len <= 1 || chapters.size < 2 || isLive) emptyList()
        else chapters.filter { it.ms > 1000 }.map { (it.ms.toFloat() / len).coerceIn(0f, 1f) }
    }

    private fun chapterAt(ms: Long) = chapters.lastOrNull { it.ms <= ms + 1 }

    fun nextChapter() {
        if (isLive) return
        val pos = maxOf(0, engine.timeMs)
        val next = chapters.firstOrNull { it.ms > pos + 1000 } ?: run { flash("No more chapters"); return }
        seekToMs(next.ms)
        flash(next.title ?: "Chapter ${chapters.indexOf(next) + 1}")
    }

    fun previousChapter() {
        if (isLive || chapters.isEmpty()) return
        val pos = maxOf(0, engine.timeMs)
        val cur = chapters.lastOrNull { it.ms <= pos }
        // Just after a chapter starts, go to the one before (like a disc player).
        val target = if (cur != null && pos - cur.ms < 3000) chapters.lastOrNull { it.ms < cur.ms } ?: cur else cur
        target ?: return
        seekToMs(target.ms)
        flash(target.title ?: "Chapter ${chapters.indexOf(target) + 1}")
    }

    // ===================== Short on-screen messages =====================

    private var flashJob: Job? = null

    fun flash(text: String, ms: Long = 1600) {
        flashJob?.cancel()
        osdMessage = text
        flashJob = scope.launch { delay(ms); osdMessage = null }
    }

    // ===================== Fine-tuning: speed and subtitle size =====================

    private val speeds = doubleArrayOf(0.5, 0.75, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0)

    private fun applySpeed(v: Double) { playbackSpeed = v; engine.setSpeed(v); settings.playbackSpeed = v }
    fun slower() { applySpeed(speeds.lastOrNull { it < playbackSpeed - 0.001 } ?: speeds[0]); flash("Speed $speedText") }
    fun faster() { applySpeed(speeds.firstOrNull { it > playbackSpeed + 0.001 } ?: speeds.last()); flash("Speed $speedText") }
    private fun applySubScale(v: Double) { subtitleScale = v; settings.subtitleScale = v }
    fun smallerSubtitles() = applySubScale(maxOf(0.5, Math.round((subtitleScale - 0.1) * 100) / 100.0))
    fun largerSubtitles() = applySubScale(minOf(2.5, Math.round((subtitleScale + 0.1) * 100) / 100.0))
    fun resetTuning() { applySpeed(1.0); applySubScale(1.0); flash("Back to normal") }

    // ===================== Trickplay (scrubbing thumbnails) =====================

    private var trick: Triple<String, String, TrickplayInfo>? = null
    private val trickSheets = LinkedHashMap<Int, Bitmap?>()
    private val trickLoading = HashSet<Int>()
    private var trickWantedMs = -1L

    private suspend fun loadTrickplay(itemId: String, sourceId: String) {
        trick = null
        trickSheets.clear(); trickLoading.clear()
        trickImage = null
        val all = AppServices.jellyfin.getTrickplay(itemId)
        if (item?.id != itemId) return
        val layouts = all?.get(sourceId) ?: all?.values?.firstOrNull()
        if (layouts.isNullOrEmpty()) { Log.info("trickplay: none for this item"); return }
        // Smallest layout that still looks sharp in the preview (about 320 px wide).
        val info = layouts.values.filter { it.width >= 280 }.minByOrNull { it.width } ?: layouts.values.maxBy { it.width }
        if (info.interval <= 0 || info.tileWidth <= 0 || info.tileHeight <= 0 || info.width <= 0) return
        trick = Triple(itemId, sourceId, info)
        trickAspect = info.width.toFloat() / maxOf(1, info.height)
    }

    /** Shows the scrubbing preview for a position. */
    fun showTrickplay(ms: Long) {
        if (isLive || lengthMs <= 1) return
        val m = ms.coerceIn(0, lengthMs)
        val chapter = chapterAt(m)?.title
        trickTime = Format.clock(m) + (chapter?.let { "  ·  $it" } ?: "")
        trickWantedMs = m
        val t = trick ?: run { trickImage = null; return }
        val info = t.third
        val n = (m / info.interval).toInt().coerceIn(0, maxOf(0, info.thumbnailCount - 1))
        val perSheet = info.tileWidth * info.tileHeight
        val sheet = n / perSheet
        val k = n % perSheet
        if (trickSheets.containsKey(sheet)) {
            val bmp = trickSheets.remove(sheet)
            trickSheets[sheet] = bmp // most recently used last
            if (bmp == null) { trickImage = null; return }
            val x = k % info.tileWidth * info.width
            val y = k / info.tileWidth * info.height
            if (x + info.width > bmp.width || y + info.height > bmp.height) { trickImage = null; return }
            trickImage = bmp
            trickRect = android.graphics.Rect(x, y, x + info.width, y + info.height)
            return
        }
        if (trickLoading.add(sheet)) scope.launch { loadTrickSheet(t.first, t.second, info, sheet) }
    }

    private suspend fun loadTrickSheet(itemId: String, sourceId: String, info: TrickplayInfo, sheet: Int) {
        val url = AppServices.jellyfin.trickplayTileUrl(itemId, sourceId, info.width, sheet)
        val bmp = runCatching {
            val r = AppServices.images.execute(ImageRequest.Builder(AppServices.context).data(url).allowHardware(false).build())
            (r as? SuccessResult)?.image?.toBitmap()
        }.getOrNull()
        if (item?.id != itemId || disposed) return
        // Keep only a few sheets in memory (each holds up to 100 thumbnails).
        while (trickSheets.size >= 6) trickSheets.remove(trickSheets.keys.first())
        trickSheets[sheet] = bmp
        trickLoading.remove(sheet)
        if (trickWantedMs >= 0) showTrickplay(trickWantedMs)
    }

    fun hideTrickplay() { trickWantedMs = -1; trickImage = null; trickTime = "" }
    val isTrickVisible get() = trickWantedMs >= 0

    // ===================== Subtitles found by the server =====================

    /** Jellyfin wants three-letter codes ("eng"); settings may hold two-letter ones ("en"). */
    private fun threeLetter(code: String?): String {
        if (code.isNullOrBlank()) return "eng"
        val c = code.trim()
        if (c.length == 3) return c.lowercase()
        return runCatching { Locale.forLanguageTag(c).isO3Language }.getOrNull()?.takeIf { it.isNotEmpty() } ?: "eng"
    }

    fun findSubtitles() {
        val it = item ?: return
        if (isLive) return
        if (subtitleSearchLanguage.isBlank()) subtitleSearchLanguage = threeLetter(subLang ?: audioLang)
        isSubtitleSearchOpen = true
        isSubtitleSearching = true
        subtitleSearchStatus = null
        remoteSubtitles.clear()
        scope.launch {
            try {
                val found = AppServices.jellyfin.searchRemoteSubtitles(it.id, threeLetter(subtitleSearchLanguage))
                if (item !== it) return@launch
                remoteSubtitles.addAll(found.sortedWith(compareByDescending<RemoteSubtitleInfo> { s -> s.isHashMatch == true }
                    .thenByDescending { s -> s.downloadCount ?: 0 }).take(40).map { s -> RemoteSub(s) })
                subtitleSearchStatus = if (remoteSubtitles.isEmpty())
                    "Nothing found. Your server needs a subtitle provider plugin (such as Open Subtitles) for this to work." else null
            } catch (e: ApiException) {
                subtitleSearchStatus = if (e.statusCode == 403) "Your Jellyfin account isn't allowed to manage subtitles. An administrator can allow it in the user's settings." else e.message
            } finally { isSubtitleSearching = false }
        }
    }

    fun closeSubtitleSearch() { isSubtitleSearchOpen = false }

    /** The server downloads the file and stores it with the item; then it's added to what's playing. */
    fun downloadSubtitle(sub: RemoteSub) {
        val it = item ?: return
        val sourceId = mediaSourceId ?: return
        val subId = sub.info.id ?: return
        if (sub.isDownloading) return
        sub.isDownloading = true
        subtitleSearchStatus = "Downloading…"
        scope.launch {
            try {
                val known = externalSubs.map { s -> s.streamIndex }.toSet()
                AppServices.jellyfin.downloadRemoteSubtitle(it.id, subId)
                // The server saves the file and rescans the item: wait for the new stream to appear.
                for (attempt in 0 until 8) {
                    if (item !== it) return@launch
                    delay(if (attempt == 0) 600 else 1200)
                    val fresh = AppServices.jellyfin.getItem(it.id)
                    val source = fresh.mediaSources?.firstOrNull { s -> s.id == sourceId } ?: fresh.mediaSources?.firstOrNull()
                    val added = source?.mediaStreams?.firstOrNull { s -> s.type == "Subtitle" && s.isExternal && s.index !in known }
                    if (source?.id == null || added == null) continue
                    val ext = ExternalSub(AppServices.jellyfin.externalSubtitleUrl(it.id, source.id!!, added), added.displayTitle ?: sub.name, added.language, added.index)
                    externalSubs += ext
                    engine.addSubtitle(ext)
                    isSubtitleSearchOpen = false
                    flash("Subtitles added")
                    delay(800)
                    loadTracks()
                    return@launch
                }
                subtitleSearchStatus = "Downloaded, but the server hasn't listed it yet. It will be there next time you play this."
            } catch (e: ApiException) { subtitleSearchStatus = "Couldn't download: ${e.message}" }
            finally { sub.isDownloading = false }
        }
    }

    // ===================== Remote control (from other Jellyfin apps) =====================

    fun isShowingItem(itemId: String) = item?.id == itemId
    fun remotePause(pause: Boolean?) { val target = pause ?: engine.isPlaying; if (target == !engine.isPlaying) return; togglePlay() }
    fun remoteSeek(ticks: Long) = seekToMs(maxOf(0, ticks) / 10_000)
    fun remoteNext() { if (hasNext) playNext() else if (isLive) channelUp() }
    fun remotePrevious() { if (isLive) channelDown() else playPrevious() }
    fun remoteSetVolume(v: Int) = changeVolume(v)
    fun remoteVolumeStep(delta: Int) = changeVolume(volume + delta)
    fun remoteMute(mute: Boolean?) { if (mute == null || mute != isMuted) toggleMute() }

    /** Selects by Jellyfin stream index (what other apps send); -1 turns subtitles off. */
    fun selectByStreamIndex(index: Int, audio: Boolean) {
        val list = if (audio) audioTracks else subtitleTracks
        val t = list.firstOrNull { it.streamIndex == index } ?: if (!audio && index < 0) list.firstOrNull { it.id < 0 } else null
        t?.let { selectTrack(it) }
    }

    fun dispose() {
        if (disposed) return
        disposed = true
        val stop = reportStopped()
        scope.launch { AppServices.settings.save() }
        Log.info("player closed")
        engine.release()
        scope.cancel()
        AppServices.scope.launch { stop?.join(); stopped.complete(Unit) }
    }
}
