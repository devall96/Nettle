package io.github.devall96.nettle.tv.player

import android.content.Context
import android.media.AudioFormat
import android.net.Uri
import android.os.Handler
import android.os.Looper
import androidx.annotation.OptIn
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.media3.common.*
import androidx.media3.common.text.Cue
import androidx.media3.common.text.CueGroup
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.DecoderReuseEvaluation
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.DefaultRenderersFactory
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.analytics.AnalyticsListener
import androidx.media3.exoplayer.audio.AudioCapabilities
import androidx.media3.exoplayer.audio.AudioSink
import androidx.media3.exoplayer.audio.DefaultAudioSink
import androidx.media3.exoplayer.mediacodec.MediaCodecSelector
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import io.github.devall96.nettle.core.services.Log
import io.github.devall96.nettle.tv.AppServices

/** Everything the player needs for one file (port of MpvLoad / PlayerLoad). */
data class PlayerLoad(
    /** The original file (Jellyfin /Videos/{id}/stream?static=true) or NostalgiaTV's broadcast feed. */
    val url: String,
    val startMs: Long = 0,
    val audioLanguage: String? = null,
    val subtitleLanguage: String? = null,
    /** Default, Always, OnlyForced, Smart or None. */
    val subtitleMode: String = "Default",
    val externalSubtitles: List<ExternalSub> = emptyList(),
    val bufferSeconds: Int = 10,
    val bufferBytes: Long? = null,
    /** "Authorization" header for the user's Jellyfin server; null for NostalgiaTV's feed. */
    val authHeader: String? = null,
)

/** An external subtitle file handed to the player, and the Jellyfin stream index it came from. */
data class ExternalSub(val url: String, val title: String, val language: String?, val streamIndex: Int)

/** One audio or subtitle track as the menu shows it. */
data class Track(val id: Int, val type: String, val name: String, val selected: Boolean, val ffIndex: Int?, val externalUrl: String?)

/**
 * Media3 (ExoPlayer) for Nettle. Only ever given the original file's direct-play URL
 * (progressive HTTP: MKV, MP4, TS…) or NostalgiaTV's MPEG-TS feed, never HLS / DASH or a
 * transcode (those modules aren't even in the app). Decodes with the TV's own MediaCodec
 * decoders on a SurfaceView, so HDR10 / HLG / HDR10+ / Dolby Vision reach the TV as they are.
 * Everything here runs on the main thread.
 */
@OptIn(UnstableApi::class)
class Engine(private val context: Context, private val passthrough: Boolean) {
    private val main = Handler(Looper.getMainLooper())
    val exo: ExoPlayer
    private var load: PlayerLoad? = null
    private val extraSubs = ArrayList<ExternalSub>()
    private var selectSubOnTracks: String? = null
    private var released = false

    // ---- state the screen reads (Compose state, main thread) ----
    var videoWidth by mutableStateOf(0); private set
    var videoHeight by mutableStateOf(0); private set
    /** True once the current load has drawn its first video frame (Live TV hides its tuning screen then). */
    var firstFrameShown by mutableStateOf(false); private set
    var cues by mutableStateOf<List<Cue>>(emptyList()); private set
    var tracks by mutableStateOf<List<Track>>(emptyList()); private set
    /** "pq", "hlg", "dovi" or null (SDR), from the format actually being decoded. */
    var hdrSignal: String? = null; private set
    var videoMime: String? = null; private set
    var frameRate: Float = 0f; private set
    /** The audio output format really in use: bitstream (passthrough) or decoded PCM, null before audio starts. */
    var audioActive = false; private set
    var audioBitstream = false; private set
    /** An audio track exists but none can be played on this device (passthrough off and no decoder). */
    var audioUnsupported = false; private set
    var ended = false; private set
    var readyOnce = false; private set
    var buffering = false; private set

    var onReady: (() -> Unit)? = null
    var onEnded: (() -> Unit)? = null
    var onError: ((String) -> Unit)? = null
    var onFrameRate: ((Float) -> Unit)? = null

    private val bitstream = setOf(
        AudioFormat.ENCODING_AC3, AudioFormat.ENCODING_E_AC3, AudioFormat.ENCODING_E_AC3_JOC, AudioFormat.ENCODING_DTS,
        AudioFormat.ENCODING_DTS_HD, AudioFormat.ENCODING_DOLBY_TRUEHD, AudioFormat.ENCODING_DTS_UHD_P2,
    )

    init {
        val renderers = object : DefaultRenderersFactory(context) {
            override fun buildAudioSink(context: Context, enableFloatOutput: Boolean, enableAudioOutputPlaybackParameters: Boolean): AudioSink {
                val b = DefaultAudioSink.Builder(context)
                    .setEnableFloatOutput(enableFloatOutput)
                    .setEnableAudioOutputPlaybackParameters(enableAudioOutputPlaybackParameters)
                val sink = b.build()
                if (passthrough) return sink
                // Passthrough off: the sink refuses encoded audio (Dolby / DTS), so the box decodes it to PCM.
                // (Media3 would otherwise bitstream whenever the TV says it can take it; tested on the onn.)
                return object : androidx.media3.exoplayer.audio.ForwardingAudioSink(sink) {
                    private fun encoded(f: Format) = f.sampleMimeType != null && f.sampleMimeType != MimeTypes.AUDIO_RAW
                    override fun supportsFormat(format: Format) = !encoded(format) && super.supportsFormat(format)
                    override fun getFormatSupport(format: Format) =
                        if (encoded(format)) AudioSink.SINK_FORMAT_UNSUPPORTED else super.getFormatSupport(format)
                }
            }
        }
        renderers.setEnableDecoderFallback(true)
        // A TV that doesn't accept Dolby Vision gets the HDR10 / SDR base layer (profiles 7 / 8),
        // decoded as plain HEVC, instead of a Dolby Vision signal it can't show.
        renderers.setMediaCodecSelector { mime, secure, tunneling ->
            if (mime == MimeTypes.VIDEO_DOLBY_VISION && !AppServices.displaySupportsDolbyVision) emptyList()
            else MediaCodecSelector.DEFAULT.getDecoderInfos(mime, secure, tunneling)
        }
        val loadControl = DefaultLoadControl.Builder()
            // Original files can be big (4K remuxes): keep a generous read-ahead, start once 2.5 s is buffered.
            .setBufferDurationsMs(15_000, 50_000, 2_500, 5_000)
            .setTargetBufferBytes(192 shl 20)
            .setPrioritizeTimeOverSizeThresholds(false)
            .build()
        exo = ExoPlayer.Builder(context, renderers)
            .setLoadControl(loadControl)
            // Refresh-rate switching is done by Nettle itself (see FrameRate), so Media3 leaves the surface alone.
            .setVideoChangeFrameRateStrategy(C.VIDEO_CHANGE_FRAME_RATE_STRATEGY_OFF)
            .setHandleAudioBecomingNoisy(true)
            .setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MOVIE).build(), true)
            .build()
        exo.addListener(object : Player.Listener {
            override fun onPlaybackStateChanged(state: Int) {
                buffering = state == Player.STATE_BUFFERING
                if (buffering && readyOnce) Log.warn("rebuffering (network too slow for this file?) at ${exo.currentPosition / 1000} s")
                if (state == Player.STATE_READY && !readyOnce) { readyOnce = true; updateOutput(); onReady?.invoke() }
                if (state == Player.STATE_ENDED && !ended) { ended = true; onEnded?.invoke() }
            }
            override fun onPlayerError(error: PlaybackException) {
                Log.warn("Media3 error ${error.errorCodeName}: ${error.message}")
                onError?.invoke(error.errorCodeName)
            }
            override fun onVideoSizeChanged(videoSize: VideoSize) {
                videoWidth = videoSize.width
                videoHeight = videoSize.height
                updateOutput()
            }
            override fun onTracksChanged(t: Tracks) { rebuildTracks(t); updateOutput() }
            override fun onCues(cueGroup: CueGroup) { cues = cueGroup.cues }
            override fun onRenderedFirstFrame() { firstFrameShown = true }
        })
        exo.addAnalyticsListener(object : AnalyticsListener {
            override fun onAudioTrackInitialized(eventTime: AnalyticsListener.EventTime, audioTrackConfig: AudioSink.AudioTrackConfig) {
                audioActive = true
                audioBitstream = audioTrackConfig.encoding in bitstream
            }
            override fun onAudioTrackReleased(eventTime: AnalyticsListener.EventTime, audioTrackConfig: AudioSink.AudioTrackConfig) { audioActive = false }
            // Diagnostics for choppy playback: dropped video frames (logged in batches) and stalls.
            override fun onDroppedVideoFrames(eventTime: AnalyticsListener.EventTime, droppedFrames: Int, elapsedMs: Long) {
                Log.warn("dropped $droppedFrames video frames in ${elapsedMs} ms (${videoWidth}x$videoHeight @${"%.3f".format(frameRate)})")
            }
            override fun onAudioUnderrun(eventTime: AnalyticsListener.EventTime, bufferSize: Int, bufferSizeMs: Long, elapsedSinceLastFeedMs: Long) {
                Log.warn("audio underrun (fed ${elapsedSinceLastFeedMs} ms ago)")
            }
            override fun onVideoInputFormatChanged(eventTime: AnalyticsListener.EventTime, format: Format, decoderReuseEvaluation: DecoderReuseEvaluation?) {
                if (format.frameRate > 0 && format.frameRate != frameRate) { frameRate = format.frameRate; onFrameRate?.invoke(frameRate) }
            }
        })
    }

    val timeMs: Long get() = if (released) 0 else exo.currentPosition
    val lengthMs: Long get() = if (released) 0 else exo.duration.coerceAtLeast(0)
    val isPlaying: Boolean get() = !released && exo.isPlaying
    val isPaused: Boolean get() = released || !exo.playWhenReady
    val isBuffering: Boolean get() = load != null && !ended && (!readyOnce || buffering)

    fun load(l: PlayerLoad) {
        load = l
        extraSubs.clear()
        ended = false; readyOnce = false; audioActive = false; audioBitstream = false; audioUnsupported = false
        hdrSignal = null; videoMime = null; frameRate = 0f
        // The last size is kept until the new stream reports its own, so the picture never snaps to
        // full screen (stretching the old frame) in between; Live TV covers the switch anyway.
        firstFrameShown = false
        cues = emptyList()
        tracks = emptyList()
        applyTrackPreferences(l)
        prepare(l.startMs)
    }

    private fun prepare(startMs: Long) {
        val l = load ?: return
        // The shared, allowlisted OkHttp client: the player can't reach any host the user didn't set up.
        val http = OkHttpDataSource.Factory(AppServices.http).setUserAgent("Nettle")
        l.authHeader?.let { http.setDefaultRequestProperties(mapOf("Authorization" to it)) }
        val subs = (l.externalSubtitles + extraSubs).map { s ->
            MediaItem.SubtitleConfiguration.Builder(Uri.parse(s.url)).setId(s.url).setLabel(s.title).setLanguage(s.language)
                .setMimeType(subtitleMime(s.url)).build()
        }
        val item = MediaItem.Builder().setUri(l.url).setSubtitleConfigurations(subs).build()
        exo.setMediaSource(DefaultMediaSourceFactory(http, TolerantExtractorsFactory()).createMediaSource(item), startMs.coerceAtLeast(0))
        exo.prepare()
        exo.playWhenReady = true
    }

    /** Jellyfin serves external subtitles as /Stream.{format}: tell Media3 the type from that. */
    private fun subtitleMime(url: String): String {
        val path = url.substringBefore('?').lowercase()
        return when {
            path.endsWith(".vtt") -> MimeTypes.TEXT_VTT
            path.endsWith(".ass") || path.endsWith(".ssa") -> MimeTypes.TEXT_SSA
            path.endsWith(".ttml") || path.endsWith(".xml") -> MimeTypes.APPLICATION_TTML
            else -> MimeTypes.APPLICATION_SUBRIP
        }
    }

    /** Jellyfin's subtitle modes and preferred languages in Media3 terms. */
    private fun applyTrackPreferences(l: PlayerLoad) {
        val b = exo.trackSelectionParameters.buildUpon()
            .clearOverrides()
            .setTrackTypeDisabled(C.TRACK_TYPE_TEXT, false)
            .setPreferredAudioLanguage(l.audioLanguage?.takeIf { it.isNotEmpty() })
            .setPreferredTextLanguage(l.subtitleLanguage?.takeIf { it.isNotEmpty() })
            .setIgnoredTextSelectionFlags(0)
            .setSelectUndeterminedTextLanguage(false)
        when (l.subtitleMode) {
            "None" -> b.setTrackTypeDisabled(C.TRACK_TYPE_TEXT, true)
            "Always" -> b.setSelectUndeterminedTextLanguage(true)
            // Only forced (foreign-dialogue) tracks: ignore language matches and "default" flags.
            "OnlyForced" -> { b.setPreferredTextLanguage(null); b.setIgnoredTextSelectionFlags(C.SELECTION_FLAG_DEFAULT) }
            // Smart: subtitles only when the audio is in another language than the subtitle preference.
            "Smart" -> if (l.audioLanguage != null && l.subtitleLanguage != null && l.audioLanguage.equals(l.subtitleLanguage, true))
                b.setIgnoredTextSelectionFlags(C.SELECTION_FLAG_DEFAULT).setPreferredTextLanguage(null)
        }
        exo.trackSelectionParameters = b.build()
    }

    fun setPause(pause: Boolean) { if (!released) exo.playWhenReady = !pause }
    fun seekTo(ms: Long) { if (!released) exo.seekTo(ms.coerceAtLeast(0)) }

    private var volume = 100
    private var muted = false
    fun setVolume(v: Int) { volume = v.coerceIn(0, 100); applyVolume() }
    fun setMute(m: Boolean) { muted = m; applyVolume() }
    private fun applyVolume() { if (!released) exo.volume = if (muted) 0f else volume / 100f }

    fun setSpeed(speed: Double) { if (!released) exo.playbackParameters = PlaybackParameters(speed.coerceIn(0.25, 3.0).toFloat()) }

    fun selectTrack(type: String, id: Int) {
        val trackType = if (type == "audio") C.TRACK_TYPE_AUDIO else C.TRACK_TYPE_TEXT
        val b = exo.trackSelectionParameters.buildUpon()
        if (id < 0) b.setTrackTypeDisabled(trackType, true)
        else {
            val t = trackRefs.firstOrNull { it.id == id && it.type == type } ?: return
            b.setTrackTypeDisabled(trackType, false)
            b.setOverrideForType(TrackSelectionOverride(t.group.mediaTrackGroup, t.index))
        }
        exo.trackSelectionParameters = b.build()
    }

    /** Adds a subtitle file mid-playback (Media3 needs the item rebuilt), then selects it. */
    fun addSubtitle(sub: ExternalSub) {
        extraSubs += sub
        selectSubOnTracks = sub.url
        val pos = exo.currentPosition
        val playing = exo.playWhenReady
        prepare(pos)
        exo.playWhenReady = playing
    }

    private class TrackRef(val id: Int, val type: String, val group: Tracks.Group, val index: Int)
    private var trackRefs: List<TrackRef> = emptyList()
    private val sourcePrefix = Regex("^(\\d+):(.*)$")

    private fun rebuildTracks(t: Tracks) {
        val refs = ArrayList<TrackRef>()
        val list = ArrayList<Track>()
        var audioId = 0
        var subId = 0
        var audioGroups = 0
        var audioSupported = 0
        for (group in t.groups) {
            val type = when (group.type) { C.TRACK_TYPE_AUDIO -> "audio"; C.TRACK_TYPE_TEXT -> "sub"; else -> null } ?: continue
            for (i in 0 until group.length) {
                if (type == "audio") audioGroups++
                if (!group.isTrackSupported(i)) continue
                if (type == "audio") audioSupported++
                val f = group.getTrackFormat(i)
                // Ids from a merged source (file + external subtitles) look like "0:2" or "1:http://…".
                val raw = f.id ?: ""
                val local = sourcePrefix.matchEntire(raw)?.groupValues?.get(2) ?: raw
                val external = if (local.startsWith("http", true)) local else null
                // Matroska / MP4 track numbers start at 1; Jellyfin's stream index is the 0-based ffprobe index.
                val ff = if (external == null) local.toIntOrNull()?.minus(1) else null
                val forced = f.selectionFlags and C.SELECTION_FLAG_FORCED != 0
                val name = describeTrack(f, external != null, type == "sub" && forced)
                val id = if (type == "audio") ++audioId else ++subId
                refs += TrackRef(id, type, group, i)
                list += Track(id, type, name, group.isTrackSelected(i), ff, external)
            }
        }
        trackRefs = refs
        tracks = list
        audioUnsupported = audioGroups > 0 && audioSupported == 0
        val wanted = selectSubOnTracks
        if (wanted != null) list.firstOrNull { it.externalUrl == wanted }?.let { selectSubOnTracks = null; selectTrack("sub", it.id) }
    }

    private fun describeTrack(f: Format, external: Boolean, forced: Boolean): String {
        val lang = f.language?.takeIf { it.isNotEmpty() && it != "und" }?.let { code ->
            runCatching { java.util.Locale.forLanguageTag(code).getDisplayLanguage(java.util.Locale.ENGLISH) }.getOrNull()?.takeIf { it.isNotEmpty() } ?: code
        }
        val codec = codecName(f.sampleMimeType)
        val channels = if (f.channelCount > 0) when (f.channelCount) { 1 -> "Mono"; 2 -> "Stereo"; 6 -> "5.1"; 8 -> "7.1"; else -> "${f.channelCount} ch" } else null
        val parts = listOfNotNull(f.label?.takeIf { it.isNotEmpty() }, lang, codec.uppercase().takeIf { it.isNotEmpty() }, channels,
            if (forced) "Forced" else null, if (external) "External" else null).distinct()
        return parts.joinToString(" · ").ifEmpty { "Track" }
    }

    private fun codecName(mime: String?): String = when (mime) {
        "audio/ac3" -> "ac3"
        "audio/eac3" -> "eac3"
        "audio/eac3-joc" -> "eac3 atmos"
        "audio/true-hd" -> "truehd"
        "audio/vnd.dts" -> "dts"
        "audio/vnd.dts.hd" -> "dts-hd"
        "audio/vnd.dts.uhd;profile=p2" -> "dts:x"
        "audio/mp4a-latm" -> "aac"
        "audio/opus" -> "opus"
        "audio/flac" -> "flac"
        "audio/raw" -> "pcm"
        "application/x-subrip" -> "srt"
        "text/x-ssa" -> "ass"
        "text/vtt" -> "vtt"
        "application/pgs" -> "pgs"
        "application/vobsub" -> "vobsub"
        "application/dvbsubs" -> "dvb"
        else -> mime?.substringAfterLast('/') ?: ""
    }

    private fun updateOutput() {
        val v = exo.videoFormat ?: return
        videoMime = v.sampleMimeType
        hdrSignal = when {
            v.sampleMimeType == MimeTypes.VIDEO_DOLBY_VISION -> "dovi"
            v.colorInfo?.colorTransfer == C.COLOR_TRANSFER_ST2084 -> "pq"
            v.colorInfo?.colorTransfer == C.COLOR_TRANSFER_HLG -> "hlg"
            else -> null
        }
        if (v.frameRate > 0 && v.frameRate != frameRate) { frameRate = v.frameRate; onFrameRate?.invoke(frameRate) }
    }

    fun describeOutput(): String {
        val v = exo.videoFormat
        val a = exo.audioFormat
        return "video ${v?.sampleMimeType ?: "-"} ${v?.codecs ?: ""} ${videoWidth}x$videoHeight ${hdrSignal ?: "sdr"} @${"%.3f".format(frameRate)}, " +
            "audio ${a?.sampleMimeType ?: "-"} ${a?.channelCount ?: 0}ch → ${if (audioBitstream) "bitstream" else "PCM"}"
    }

    fun release() {
        if (released) return
        released = true
        runCatching { exo.release() }
    }
}
