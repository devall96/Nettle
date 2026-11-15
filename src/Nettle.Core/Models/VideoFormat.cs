namespace Nettle.Core.Models;

/// <summary>What a media tag in the player is about (each kind can be switched off in Settings).</summary>
public enum MediaTagKind { Resolution, Range, Codec, Audio }

/// <summary>Human-friendly HDR / resolution labels from Jellyfin's stream metadata.</summary>
public static class VideoFormat
{
    /// <summary>Short tag for the video format ("HEVC", "H.264", "AV1"), or null when unknown.</summary>
    public static string? CodecTag(string? codec) => codec?.ToLowerInvariant() switch
    {
        null or "" => null,
        "hevc" or "h265" => "HEVC",
        "h264" or "avc" => "H.264",
        "av1" => "AV1",
        "vp9" => "VP9",
        "vp8" => "VP8",
        "mpeg2video" => "MPEG-2",
        "mpeg4" => "MPEG-4",
        "vc1" => "VC-1",
        var c => c.ToUpperInvariant(),
    };

    /// <summary>Short tag for the audio track: format plus channels ("ATMOS 7.1", "DD+ 5.1", "AAC 2.0"), or null when unknown.</summary>
    public static string? AudioTag(MediaStream? a)
    {
        if (a == null) return null;
        string? name = AudioLabel(a) switch
        {
            "Dolby Atmos" => "ATMOS",
            "DTS:X" => "DTS:X",
            "Dolby TrueHD" => "TRUEHD",
            "Dolby Digital Plus" => "DD+",
            "Dolby Digital" => "DD",
            "DTS-HD MA" => "DTS-HD MA",
            "DTS-HD" => "DTS-HD",
            "DTS" => "DTS",
            _ => null,
        };
        name ??= a.Codec?.ToLowerInvariant() switch
        {
            null or "" => null,
            var c when c.StartsWith("pcm", StringComparison.Ordinal) => "PCM",
            var c => c.ToUpperInvariant(),
        };
        if (name == null) return null;
        var channels = a.Channels switch { >= 8 => "7.1", >= 6 => "5.1", 2 => "2.0", 1 => "1.0", _ => null };
        return channels == null ? name : name + " " + channels;
    }

    /// <summary>
    /// Maps Jellyfin's VideoRangeType to a badge. Returns null for SDR / unknown.
    /// <paramref name="warning"/> is set for formats the player can't render correctly (none
    /// currently: mpv applies Dolby Vision metadata itself, including profile 5).
    /// </summary>
    public static string? HdrLabel(string? rangeType, out string? warning)
    {
        warning = null;
        return rangeType switch
        {
            "HDR10" => "HDR10",
            "HDR10Plus" => "HDR10+",
            "HLG" => "HLG",
            // Every Dolby Vision type (incl. 10.11's DOVIWithEL / DOVIWithELHDR10Plus), but not DOVIInvalid
            // (broken DV metadata: nothing is claimed for it).
            "DOVIInvalid" => null,
            { } r when r.StartsWith("DOVI", StringComparison.Ordinal) => "Dolby Vision",
            _ => null,
        };
    }

    /// <summary>True for Dolby Vision profile 5 (no HDR10/SDR fallback layer).</summary>
    public static bool IsDolbyVisionOnly(string? rangeType) => rangeType == "DOVI";

    /// <summary>Premium audio format name for a badge, or null for ordinary stereo/AAC etc.</summary>
    public static string? AudioLabel(MediaStream? a)
    {
        if (a == null) return null;
        var text = $"{a.DisplayTitle} {a.Title} {a.Profile}";
        bool has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);
        var codec = a.Codec?.ToLowerInvariant();
        if (has("atmos")) return "Dolby Atmos";
        if (has("dts:x") || has("dts-x")) return "DTS:X";
        return codec switch
        {
            "truehd" => "Dolby TrueHD",
            "eac3" => "Dolby Digital Plus",
            "ac3" => "Dolby Digital",
            // Jellyfin's profile / title say "DTS-HD MA" or "DTS-HD HRA"; a bare "ma" can be any word ("German").
            "dts" when has("dts-hd ma") || has("dts-hd master") => "DTS-HD MA",
            "dts" when has("dts-hd") => "DTS-HD",
            "dts" => "DTS",
            "flac" when a.Channels >= 6 => "Lossless " + ChannelLayout(a.Channels),
            _ => null
        };
    }

    public static string ChannelLayout(int? ch) => ch switch
    {
        >= 8 => "7.1",
        >= 6 => "5.1",
        2 => "Stereo",
        1 => "Mono",
        null => "",
        _ => ch + " ch"
    };

    /// <summary>
    /// Width decides for wide pictures (a 1920×800 film is 1080p); height, when known, rescues narrow ones
    /// (1440×1080 is 1080p too, not 720p).
    /// </summary>
    public static string? Resolution(int? width, int? height = null)
    {
        int byWidth = width switch { >= 3800 => 4, >= 2500 => 3, >= 1900 => 2, >= 1200 => 1, > 0 => 0, _ => -1 };
        int byHeight = height switch { >= 2000 => 4, >= 1400 => 3, >= 1000 => 2, >= 700 => 1, > 0 => 0, _ => -1 };
        return Math.Max(byWidth, byHeight) switch { 4 => "4K", 3 => "1440p", 2 => "1080p", 1 => "720p", 0 => "SD", _ => null };
    }
}
