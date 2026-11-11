using System.Runtime.InteropServices;

namespace Nettle.Desktop;

/// <summary>
/// Tells the Microsoft Store copy of Nettle (an MSIX package) from the Setup / portable copy.
/// A packaged copy is installed and updated by Windows, so Nettle's own updater stays off there.
/// Asks Windows only; nothing is sent anywhere.
/// </summary>
internal static class StorePackage
{
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    /// <summary>True when this process runs with package identity (installed as an MSIX package).</summary>
    public static bool IsPackaged
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                var length = 0;
                return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
            }
            catch { return false; }
        }
    }
}
