using Nettle.Core.Platform;

namespace Nettle.Desktop.Linux;

/// <summary>Linux paths follow the XDG base-directory spec (~/.local/share, ~/.cache).</summary>
internal sealed class LinuxPlatformInfo : IPlatformInfo
{
    public string DataDirectory { get; } = DataRoot();
    public string CacheDirectory { get; } = CacheRoot();

    private static string DataRoot()
    {
#if DEBUG || NETTLE_DEV
        var dev = Environment.GetEnvironmentVariable("NETTLE_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return dev;
#endif
        // .NET maps LocalApplicationData to $XDG_DATA_HOME or ~/.local/share.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nettle");
    }

    private static string CacheRoot()
    {
#if DEBUG || NETTLE_DEV
        var dev = Environment.GetEnvironmentVariable("NETTLE_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return Path.Combine(dev, "Cache");
#endif
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(root, "Nettle");
    }

    /// <summary>Brings over the data and cache folders from the old name (see <see cref="LegacyData"/>). Run before anything uses them.</summary>
    public static void MoveLegacyData()
    {
#if DEBUG || NETTLE_DEV
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NETTLE_PROFILE_DIR"))) return;
#endif
        var data = DataRoot();
        var cache = CacheRoot();
        LegacyData.MoveFolder(Path.Combine(Path.GetDirectoryName(data)!, LegacyData.OldName), data, "data");
        LegacyData.MoveFolder(Path.Combine(Path.GetDirectoryName(cache)!, LegacyData.OldName), cache, "cache");
    }

    // Privacy: servers see a generic device name, never this PC's hostname.
    public string DeviceName { get; } = "Nettle (Linux)";
    public string PlatformName => "Linux";
}
