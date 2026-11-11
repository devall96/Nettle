namespace Nettle.Desktop;

/// <summary>
/// Settings → "Match HDR to the video" (Windows): like a TV, the monitor goes into HDR for an HDR video and out of it
/// for an SDR one, and back to how it was when the player closes. A note in the data folder records what to put back,
/// so even a crash mid-film doesn't leave the screen switched (it's restored at the next start).
/// </summary>
internal sealed class HdrSwitcher
{
    private readonly string _marker;
    private readonly object _gate = new();
    private string? _monitor;     // the monitor Nettle switched (GDI name)
    private bool _originalOn;     // its HDR state before Nettle touched it

    public HdrSwitcher(string dataDir) => _marker = Path.Combine(dataDir, "hdr-restore.txt");

    /// <summary>Puts the screen back if a previous run switched it and couldn't (crash, power loss).</summary>
    public void RecoverAfterCrash()
    {
        try
        {
            if (!File.Exists(_marker)) return;
            var parts = File.ReadAllText(_marker).Trim().Split('|');
            File.Delete(_marker);
            if (parts.Length != 2 || !bool.TryParse(parts[1], out var original)) return;
            if (DisplayHdr.Info(parts[0]) is { } now && now.On != original && DisplayHdr.SetHdr(parts[0], original))
                Nettle.Core.Services.Log.Info($"HDR: put {parts[0]} back to {(original ? "HDR" : "SDR")} after the last run");
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("HDR restore after crash failed: " + ex.Message); }
    }

    /// <summary>
    /// <paramref name="hdr"/>: the video is HDR (true) or SDR (false); null = the player closed, put the screen back.
    /// Returns true when the monitor really switched (it goes dark for a moment; wait before starting the video).
    /// </summary>
    public bool Apply(string? monitor, bool? hdr)
    {
        lock (_gate)
        {
            if (hdr == null || monitor == null) return Restore();
            if (DisplayHdr.Info(monitor) is not { Supported: true } info) return false; // not an HDR monitor
            if (info.On == hdr.Value) return false;
            if (_monitor != null && !string.Equals(_monitor, monitor, StringComparison.OrdinalIgnoreCase)) Restore();
            if (_monitor == null)
            {
                _monitor = monitor;
                _originalOn = info.On;
                try { File.WriteAllText(_marker, $"{monitor}|{info.On}"); } catch { }
            }
            bool ok = DisplayHdr.SetHdr(monitor, hdr.Value);
            Nettle.Core.Services.Log.Info($"HDR: {monitor} {(hdr.Value ? "on" : "off")} for this video" + (ok ? "" : " (Windows refused)"));
            return ok;
        }
    }

    /// <summary>Back to the state before Nettle switched anything (no-op if it didn't).</summary>
    public bool Restore()
    {
        lock (_gate)
        {
            if (_monitor is not { } m) return false;
            _monitor = null;
            bool changed = false;
            if (DisplayHdr.Info(m) is { } now && now.On != _originalOn)
            {
                changed = DisplayHdr.SetHdr(m, _originalOn);
                Nettle.Core.Services.Log.Info($"HDR: {m} back to {(_originalOn ? "HDR" : "SDR")}");
            }
            try { File.Delete(_marker); } catch { }
            return changed;
        }
    }
}
