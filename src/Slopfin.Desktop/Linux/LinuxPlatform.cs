using Slopfin.Core.Platform;

namespace Slopfin.Desktop.Linux;

/// <summary>Linux paths follow the XDG base-directory spec (~/.local/share, ~/.cache).</summary>
internal sealed class LinuxPlatformInfo : IPlatformInfo
{
    public string DataDirectory { get; } = DataRoot();
    public string CacheDirectory { get; } = CacheRoot();

    private static string DataRoot()
    {
#if DEBUG
        var dev = Environment.GetEnvironmentVariable("SLOPFIN_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return dev;
#endif
        // .NET maps LocalApplicationData to $XDG_DATA_HOME or ~/.local/share.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slopfin");
    }

    private static string CacheRoot()
    {
#if DEBUG
        var dev = Environment.GetEnvironmentVariable("SLOPFIN_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return Path.Combine(dev, "Cache");
#endif
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(root, "Slopfin");
    }

    // Privacy: servers see a generic device name, never this PC's hostname.
    public string DeviceName { get; } = "Slopfin (Linux)";
    public string PlatformName => "Linux";
}
