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

    fun resolution(width: Int?): String? = when {
        width == null -> null
        width >= 3800 -> "4K"
        width >= 2500 -> "1440p"
        width >= 1900 -> "1080p"
        width >= 1200 -> "720p"
        width > 0 -> "SD"
        else -> null
    }
}
