namespace Nettle.UI;

/// <summary>
/// Wording and switches that differ per platform head, so shared screens never check the OS
/// themselves. Desktop's defaults are picked from the running OS; other heads (Android TV)
/// replace the whole profile at startup.
/// </summary>
public sealed record PlatformProfile
{
    /// <summary>Playback engine name as shown to the user ("mpv", "Media3").</summary>
    public string EngineName { get; init; } = "mpv";
    public string HardwareDecodingHint { get; init; } = "Use the GPU (D3D11VA on Windows, VA-API on Linux) for smooth 4K/HDR playback";
    public bool ShowHardwareDecoding { get; init; } = true;
    public string PassthroughHint { get; init; } = "";
    public string SecureStoreText { get; init; } = "";
    /// <summary>Badge detail for HDR video shown in SDR because the display isn't in HDR mode.</summary>
    public string SdrDisplayNote { get; init; } = "";
    /// <summary>Badge detail for HDR video when the platform can't output HDR at all (null: it can).</summary>
    public string? NoHdrOutputNote { get; init; }
    /// <summary>10-foot UI: bigger focus rings, no hover-only affordances, no window controls.</summary>
    public bool IsTv { get; init; }
    public bool ShowUpdates { get; init; } = true;
    /// <summary>Video is drawn on a surface below the app's UI, so backgrounds behind the player must be see-through.</summary>
    public bool VideoBehindUi { get; init; }
    public string AboutPlayback { get; init; } = "Playback is powered by mpv (GPL-2.0+).";

    public static PlatformProfile Desktop() => OperatingSystem.IsWindows()
        ? new()
        {
            PassthroughHint = "Sends Dolby (incl. Atmos and TrueHD) and DTS (incl. DTS-HD and DTS:X) untouched to your AV receiver or soundbar over HDMI / S/PDIF. While it plays, Nettle's volume control has no effect. For TrueHD and DTS-HD only, other apps' sound is paused (Windows needs exclusive use of the device for those). Leave off for TV speakers and headphones.",
            SecureStoreText = "Saved profiles on this PC (tokens encrypted with Windows DPAPI):",
            SdrDisplayNote = "Display is in SDR, so this is tone-mapped. Turn on Windows HDR for true HDR.",
        }
        : new()
        {
            PassthroughHint = "Sends Dolby (incl. Atmos and TrueHD) and DTS (incl. DTS-HD and DTS:X) untouched to your AV receiver or soundbar over HDMI / S/PDIF. Your sound server must allow passthrough for that output. Nettle's volume control has no effect on it. Leave off for TV speakers and headphones.",
            SecureStoreText = "Saved profiles on this PC (tokens kept in your desktop keyring):",
            SdrDisplayNote = "Tone-mapped to SDR (HDR output isn't available on Linux yet)",
            NoHdrOutputNote = "Tone-mapped to SDR (HDR output isn't available on Linux yet)",
        };
}
