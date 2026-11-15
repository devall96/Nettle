package io.github.devall96.nettle.core.models

/** Human-friendly HDR / resolution labels from Jellyfin's stream metadata (port of VideoFormat.cs). */
object VideoFormat {
    /**
     * Maps Jellyfin's VideoRangeType to a badge, or null for SDR / unknown. The second value is a
     * warning for formats this device can't show correctly (Dolby Vision profile 5 on a TV that
     * doesn't take Dolby Vision has no fallback layer).
     */
    fun hdrLabel(rangeType: String?, displaySupportsDolbyVision: Boolean = true): Pair<String?, String?> {
        val label = when (rangeType) {
            "HDR10" -> "HDR10"
            "HDR10Plus" -> "HDR10+"
            "HLG" -> "HLG"
            // Every Dolby Vision type (incl. 10.11's DOVIWithEL / DOVIWithELHDR10Plus), but not DOVIInvalid.
            "DOVIInvalid" -> null
            in DV_PREFIXED -> "Dolby Vision"
            else -> null
        }
        val warning = if (isDolbyVisionOnly(rangeType) && !displaySupportsDolbyVision)
            "Dolby Vision profile 5 has no HDR10 layer, and this TV doesn't accept Dolby Vision, so colours may look wrong."
        else null
        return label to warning
    }

    private object DV_PREFIXED { operator fun contains(r: String?) = r != null && r.startsWith("DOVI") }

    /** True for Dolby Vision profile 5 (no HDR10/SDR fallback layer). */
    fun isDolbyVisionOnly(rangeType: String?): Boolean = rangeType == "DOVI"

    /** Premium audio format name for a badge, or null for ordinary stereo/AAC etc. */
    fun audioLabel(a: MediaStream?): String? {
        if (a == null) return null
        val text = "${a.displayTitle} ${a.title} ${a.profile}"
        fun has(s: String) = text.contains(s, ignoreCase = true)
        val codec = a.codec?.lowercase()
        if (has("atmos")) return "Dolby Atmos"
        if (has("dts:x") || has("dts-x")) return "DTS:X"
        return when {
            codec == "truehd" -> "Dolby TrueHD"
            codec == "eac3" -> "Dolby Digital Plus"
            codec == "ac3" -> "Dolby Digital"
            // Jellyfin's profile / title say "DTS-HD MA" or "DTS-HD HRA"; a bare "ma" can be any word ("German").
            codec == "dts" && (has("dts-hd ma") || has("dts-hd master")) -> "DTS-HD MA"
            codec == "dts" && has("dts-hd") -> "DTS-HD"
            codec == "dts" -> "DTS"
            codec == "flac" && (a.channels ?: 0) >= 6 -> "Lossless " + channelLayout(a.channels)
            else -> null
        }
    }

    fun channelLayout(ch: Int?): String = when {
        ch == null -> ""
        ch >= 8 -> "7.1"
        ch >= 6 -> "5.1"
        ch == 2 -> "Stereo"
        ch == 1 -> "Mono"
        else -> "$ch ch"
    }

    /** Short tag for the video format ("HEVC", "H.264", "AV1"), or null when unknown. */
    fun codecTag(codec: String?): String? = when (val c = codec?.lowercase()) {
        null, "" -> null
        "hevc", "h265" -> "HEVC"
        "h264", "avc" -> "H.264"
        "av1" -> "AV1"
        "vp9" -> "VP9"
        "vp8" -> "VP8"
        "mpeg2video" -> "MPEG-2"
        "mpeg4" -> "MPEG-4"
        "vc1" -> "VC-1"
        else -> c.uppercase()
    }

    /** Short tag for the audio track: format plus channels ("ATMOS 7.1", "DD+ 5.1", "AAC 2.0"), or null when unknown. */
    fun audioTag(a: MediaStream?): String? {
        if (a == null) return null
        val name = when (audioLabel(a)) {
            "Dolby Atmos" -> "ATMOS"
            "DTS:X" -> "DTS:X"
            "Dolby TrueHD" -> "TRUEHD"
            "Dolby Digital Plus" -> "DD+"
            "Dolby Digital" -> "DD"
            "DTS-HD MA" -> "DTS-HD MA"
            "DTS-HD" -> "DTS-HD"
            "DTS" -> "DTS"
            else -> null
        } ?: a.codec?.lowercase()?.takeIf { it.isNotEmpty() }?.let { if (it.startsWith("pcm")) "PCM" else it.uppercase() } ?: return null
        val ch = a.channels ?: 0
        val channels = when { ch >= 8 -> "7.1"; ch >= 6 -> "5.1"; ch == 2 -> "2.0"; ch == 1 -> "1.0"; else -> null }
        return if (channels == null) name else "$name $channels"
    }

    /**
     * Width decides for wide pictures (a 1920×800 film is 1080p); height, when known, rescues narrow ones
     * (1440×1080 is 1080p too, not 720p).
     */
    fun resolution(width: Int?, height: Int? = null): String? {
        val w = width ?: 0
        val h = height ?: 0
        val byWidth = when { w >= 3800 -> 4; w >= 2500 -> 3; w >= 1900 -> 2; w >= 1200 -> 1; w > 0 -> 0; else -> -1 }
        val byHeight = when { h >= 2000 -> 4; h >= 1400 -> 3; h >= 1000 -> 2; h >= 700 -> 1; h > 0 -> 0; else -> -1 }
        return when (maxOf(byWidth, byHeight)) { 4 -> "4K"; 3 -> "1440p"; 2 -> "1080p"; 1 -> "720p"; 0 -> "SD"; else -> null }
    }
}
