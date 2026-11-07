namespace Slopfin.Core.Models;

/// <summary>Human-friendly HDR / resolution labels from Jellyfin's stream metadata.</summary>
public static class VideoFormat
{
    /// <summary>
    /// Maps Jellyfin's VideoRangeType to a badge. Returns null for SDR / unknown.
    /// <paramref name="warning"/> is set for formats libVLC 3 can't render correctly.
    /// </summary>
    public static string? HdrLabel(string? rangeType, out string? warning)
    {
        warning = null;
        switch (rangeType)
        {
            case "HDR10": return "HDR10";
            case "HDR10Plus": return "HDR10+";
            case "HLG": return "HLG";
            case "DOVIWithHDR10":
            case "DOVIWithHDR10Plus":
            case "DOVIWithHLG":
            case "DOVIWithSDR":
                return "Dolby Vision";
            case "DOVI":
                // Profile 5 (no HDR10/SDR fallback layer): VLC 3 shows incorrect colours.
                warning = "Dolby Vision profile 5 has no HDR10 fallback, so colours may look wrong (purple/green) in this player.";
                return "Dolby Vision 5";
            default:
                return null;
        }
    }

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
