namespace Nettle.Core.Models;

/// <summary>Human-friendly HDR / resolution labels from Jellyfin's stream metadata.</summary>
public static class VideoFormat
{
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
            "DOVI" or "DOVIWithHDR10" or "DOVIWithHDR10Plus" or "DOVIWithHLG" or "DOVIWithSDR" => "Dolby Vision",
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
            "dts" when has("ma") || has("hd") => "DTS-HD MA",
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

    public static string? Resolution(int? width) => width switch
    {
        >= 3800 => "4K",
        >= 2500 => "1440p",
        >= 1900 => "1080p",
        >= 1200 => "720p",
        > 0 => "SD",
        _ => null
    };
}
