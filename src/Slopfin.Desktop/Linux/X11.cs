using System.Runtime.InteropServices;
using Avalonia;

namespace Slopfin.Desktop.Linux;

/// <summary>Just enough Xlib to ask where a window really is on screen.</summary>
internal static class X11
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern int XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY, out int destX, out int destY, out IntPtr child);

    private static IntPtr _display;

    /// <summary>Top-left of the window's client area in screen pixels, or null if X11 isn't available.</summary>
    public static PixelPoint? TryGetRootPosition(IntPtr xid)
    {
        try
        {
            if (_display == IntPtr.Zero) _display = XOpenDisplay(IntPtr.Zero); // own connection, kept for the process
            if (_display == IntPtr.Zero) return null;
            return XTranslateCoordinates(_display, xid, XDefaultRootWindow(_display), 0, 0, out var x, out var y, out _) != 0
                ? new PixelPoint(x, y)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
