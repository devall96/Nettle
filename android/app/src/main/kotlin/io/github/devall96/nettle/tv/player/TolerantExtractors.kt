package io.github.devall96.nettle.tv.player

import android.net.Uri
import androidx.media3.common.util.UnstableApi
import androidx.media3.extractor.DefaultExtractorsFactory
import androidx.media3.extractor.Extractor
import androidx.media3.extractor.ExtractorInput
import androidx.media3.extractor.ExtractorsFactory
import androidx.media3.extractor.mkv.MatroskaExtractor
import androidx.media3.extractor.text.DefaultSubtitleParserFactory
import androidx.media3.extractor.text.SubtitleParser
import io.github.devall96.nettle.core.services.Log

/**
 * Media3's file readers, except that Matroska files with a zlib/bzlib/lzo-compressed track
 * (mkvmerge compresses subtitle tracks with zlib by default) still play. Stock Media3 only
 * supports header stripping and rejects the WHOLE file ("ContentCompAlgo 0 not supported"), even
 * when that track is never selected. Here only that track is dropped (logged); the video, the
 * audio and the other tracks play as usual. Still the original file over the static stream URL:
 * nothing is converted anywhere.
 */
@UnstableApi
class TolerantExtractorsFactory : ExtractorsFactory {
    private val base = DefaultExtractorsFactory()
    private var subtitleParserFactory: SubtitleParser.Factory = DefaultSubtitleParserFactory()
    private var textTranscoding = true

    override fun experimentalSetTextTrackTranscodingEnabled(enabled: Boolean): ExtractorsFactory {
        textTranscoding = enabled; base.experimentalSetTextTrackTranscodingEnabled(enabled); return this
    }

    override fun setSubtitleParserFactory(factory: SubtitleParser.Factory): ExtractorsFactory {
        subtitleParserFactory = factory; base.setSubtitleParserFactory(factory); return this
    }

    override fun createExtractors(): Array<Extractor> = swap(base.createExtractors())

    override fun createExtractors(uri: Uri, responseHeaders: Map<String, List<String>>): Array<Extractor> =
        swap(base.createExtractors(uri, responseHeaders))

    private fun swap(all: Array<Extractor>): Array<Extractor> = Array(all.size) { i ->
        if (all[i] is MatroskaExtractor) TolerantMatroskaExtractor(subtitleParserFactory, if (textTranscoding) 0 else MatroskaExtractor.FLAG_EMIT_RAW_SUBTITLE_DATA)
        else all[i]
    }
}

@UnstableApi
private class TolerantMatroskaExtractor(parser: SubtitleParser.Factory, flags: Int) : MatroskaExtractor(parser, flags) {
    private var dropTrack = false
    private val logged = HashSet<Pair<Int, String?>>()

    override fun startMasterElement(id: Int, contentPosition: Long, contentSize: Long) {
        if (id == ID_TRACK_ENTRY) dropTrack = false
        super.startMasterElement(id, contentPosition, contentSize)
    }

    override fun integerElement(id: Int, value: Long) {
        // Only header stripping (3) can be undone by Media3; any other compression drops the track instead of the file.
        if (id == ID_CONTENT_COMPRESSION_ALGORITHM && value != 3L) { dropTrack = true; return }
        super.integerElement(id, value)
    }

    override fun binaryElement(id: Int, contentSize: Int, input: ExtractorInput) {
        // The settings of a compression we can't undo are not header-stripping bytes: skip them.
        if (id == ID_CONTENT_COMPRESSION_SETTINGS && dropTrack) { input.skipFully(contentSize); return }
        super.binaryElement(id, contentSize, input)
    }

    override fun endMasterElement(id: Int) {
        if (id == ID_TRACK_ENTRY && dropTrack) {
            val track = getCurrentTrack(id)
            // Media3 re-reads the headers on every seek: say it once per file, not every time.
            if (logged.add(track.number to track.codecId)) Log.warn("mkv: skipping track ${track.number} with unsupported compression (codec ${track.codecId}); the rest of the file plays")
            track.codecId = "NETTLE/UNSUPPORTED-COMPRESSION" // Media3 ignores tracks with an unknown codec
        }
        super.endMasterElement(id)
    }

    private companion object {
        const val ID_TRACK_ENTRY = 0xAE
        const val ID_CONTENT_COMPRESSION_ALGORITHM = 0x4254
        const val ID_CONTENT_COMPRESSION_SETTINGS = 0x4255
    }
}
