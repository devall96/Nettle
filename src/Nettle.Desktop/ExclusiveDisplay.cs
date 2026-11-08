using System.Runtime.InteropServices;

namespace Nettle.Desktop;

/// <summary>
/// Exclusive full screen on Windows: switches the monitor Nettle is on to the refresh rate that
/// shows the video without judder (the same rate, or a whole multiple of it), at the monitor's
/// current resolution. Uses a temporary mode change (CDS_FULLSCREEN), which Windows undoes by
/// itself if Nettle exits or crashes; <see cref="Restore"/> puts the normal mode back.
/// </summary>
internal sealed class ExclusiveDisplay
{
    private string? _changedDevice;

    public void MatchRefreshRate(IntPtr hwnd, double? fps)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var device = DeviceFor(hwnd);
            if (device == null) return;
            if (fps is not { } f) { Restore(); return; }

            // The monitor's own (registry) mode is the baseline: modes are picked at that resolution.
            var baseline = NewDevMode();
            if (!EnumDisplaySettingsW(device, ENUM_REGISTRY_SETTINGS, ref baseline)) return;
            var modes = new List<DEVMODE>();
            for (int i = 0; ; i++)
            {
                var m = NewDevMode();
                if (!EnumDisplaySettingsW(device, i, ref m)) break;
                if (m.dmPelsWidth == baseline.dmPelsWidth && m.dmPelsHeight == baseline.dmPelsHeight && m.dmBitsPerPel == baseline.dmBitsPerPel)
                    modes.Add(m);
            }
            var best = modes
                .Select(m => (mode: m, cadence: Cadence(m.dmDisplayFrequency, f)))
                .Where(x => x.cadence > 0)
                .OrderBy(x => x.cadence)
                .ThenBy(x => Math.Abs(Hz(x.mode.dmDisplayFrequency) - f * x.cadence))
                .Select(x => (DEVMODE?)x.mode)
                .FirstOrDefault();

            var current = NewDevMode();
            EnumDisplaySettingsW(device, ENUM_CURRENT_SETTINGS, ref current);
            if (best is not { } target)
            {
                Nettle.Core.Services.Log.Info($"display mode: no judder-free rate for {f:0.###} fps on this monitor, staying at {current.dmDisplayFrequency} Hz");
                Restore();
                return;
            }
            if (target.dmDisplayFrequency == current.dmDisplayFrequency) return;
            target.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            int result = ChangeDisplaySettingsExW(device, ref target, IntPtr.Zero, CDS_FULLSCREEN, IntPtr.Zero);
            Nettle.Core.Services.Log.Info($"display mode: {current.dmPelsWidth}x{current.dmPelsHeight}@{current.dmDisplayFrequency} → @{target.dmDisplayFrequency} Hz for {f:0.###} fps" +
                (result == 0 ? "" : $" failed ({result})"));
            if (result == 0) _changedDevice = device;
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("display mode switch failed: " + ex.Message); }
    }

    /// <summary>Puts the monitor back in its normal mode (no-op if Nettle didn't change it).</summary>
    public void Restore()
    {
        if (!OperatingSystem.IsWindows() || _changedDevice is not { } device) return;
        _changedDevice = null;
        try
        {
            ChangeDisplaySettingsExW(device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            Nettle.Core.Services.Log.Info("display mode: restored");
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("display mode restore failed: " + ex.Message); }
    }

    /// <summary>Windows lists 23.976 / 29.97 / 59.94 / 119.88 Hz as 23 / 29 / 59 / 119.</summary>
    private static double Hz(int listed) => listed is 23 or 29 or 47 or 59 or 71 or 119 or 143 ? (listed + 1) / 1.001 : listed;

    /// <summary>How many refreshes each frame gets when the rate is a whole multiple of fps (0 = it isn't).</summary>
    private static int Cadence(int listed, double fps)
    {
        double hz = Hz(listed);
        int n = (int)Math.Round(hz / fps);
        return n >= 1 && Math.Abs(hz - fps * n) < 0.01 * n ? n : 0;
    }

    private static string? DeviceFor(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info) ? info.szDevice : null;
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    private const int ENUM_CURRENT_SETTINGS = -1, ENUM_REGISTRY_SETTINGS = -2;
    private const int CDS_FULLSCREEN = 0x4;
    private const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public int rcMonitorL, rcMonitorT, rcMonitorR, rcMonitorB;
        public int rcWorkL, rcWorkT, rcWorkR, rcWorkB;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string device, int modeNum, ref DEVMODE mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE mode, IntPtr hwnd, int flags, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string device, IntPtr mode, IntPtr hwnd, int flags, IntPtr param);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);
}

/// <summary>
/// Linux (X11 / XWayland) exclusive full screen: _NET_WM_BYPASS_COMPOSITOR asks the compositor to
/// stop compositing the window (unredirect), so frames go to the screen directly.
/// </summary>
internal static class LinuxCompositorHint
{
    public static void SetBypass(IntPtr xid, bool on)
    {
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero) return;
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            try
            {
                var atom = XInternAtom(display, "_NET_WM_BYPASS_COMPOSITOR", false);
                var cardinal = XInternAtom(display, "CARDINAL", false);
                // 1 = please unredirect, 0 = no preference.
                var value = new IntPtr[] { on ? 1 : 0 };
                XChangeProperty(display, xid, atom, cardinal, 32, 0 /* PropModeReplace */, value, 1);
                XFlush(display);
            }
            finally { XCloseDisplay(display); }
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("compositor bypass hint failed: " + ex.Message); }
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
}
