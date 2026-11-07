using System.Runtime.InteropServices;
using Slopfin.UI;

namespace Slopfin.Desktop;

/// <summary>
/// Asks Windows whether the monitor showing a window is currently in HDR mode
/// (Settings → Display → Use HDR). Uses DisplayConfig "advanced color" info; on Windows 11 24H2+
/// the newer INFO_2 query distinguishes real HDR from SDR auto colour management.
/// </summary>
internal static class DisplayHdr
{
    public static HdrDisplayState ForWindow(IntPtr hwnd)
    {
        try
        {
            var gdiName = MonitorDeviceName(hwnd);
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var nPath, out var nMode) != 0) return HdrDisplayState.Unknown;
            var paths = new DISPLAYCONFIG_PATH_INFO[nPath];
            var modes = new DISPLAYCONFIG_MODE_INFO[nMode];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref nPath, paths, ref nMode, modes, IntPtr.Zero) != 0) return HdrDisplayState.Unknown;

            for (int i = 0; i < nPath; i++)
            {
                var p = paths[i];
                // Match the path whose source is the GDI device (\\.\DISPLAYn) hosting our window.
                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                if (gdiName != null && !string.Equals(src.viewGdiDeviceName, gdiName, StringComparison.OrdinalIgnoreCase)) continue;

                // Windows 11 24H2+: activeColorMode tells HDR apart from SDR/WCG colour management.
                var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref info2) == 0)
                    return info2.activeColorMode == 2 ? HdrDisplayState.Hdr : HdrDisplayState.Sdr;

                // Older Windows: advancedColorEnabled bit means HDR is on.
                var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref info) == 0)
                    return (info.value & 0x2) != 0 ? HdrDisplayState.Hdr : HdrDisplayState.Sdr;
            }
        }
        catch (Exception ex)
        {
            Slopfin.Core.Services.Log.Warn("HDR display query failed: " + ex.Message);
        }
        return HdrDisplayState.Unknown;
    }

    private static string? MonitorDeviceName(IntPtr hwnd)
    {
        var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        return mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi) ? mi.szDevice : null;
    }

    // ---------------- interop ----------------
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public uint refreshNum; public uint refreshDen; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType; public uint id; public LUID adapterId;
        public ulong u0, u1, u2, u3, u4, u5; // 48-byte union (target/source/desktop image mode)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; public uint activeColorMode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint cbSize; public int l, t, r, b, wl, wt, wr, wb; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
}
