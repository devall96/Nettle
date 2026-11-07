using System.Diagnostics;
using System.Text;
using Nettle.UI;

namespace Nettle.Desktop.Linux;

/// <summary>
/// "Add Nettle to your applications menu": writes a freedesktop .desktop entry and the app icon
/// into the user's own data folder (no root, nothing system-wide). Removing deletes exactly those
/// two files. If the AppImage is later moved, the entry is quietly pointed at its new location.
/// </summary>
internal sealed class LinuxDesktopIntegration : IDesktopIntegration
{
    private const string DesktopId = "nettle.desktop";
    // Entry written by versions before the rename (Slopfin). It keeps its file name, so a launcher
    // pinned to the dock stays pinned; its contents are brought up to date on startup.
    private const string LegacyDesktopId = "slopfin.desktop";
    private const string LegacyIconName = "slopfin.png";

    private static string DataHome
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }
    }

    private static string AppsDir => Path.Combine(DataHome, "applications");
    private static string LegacyDesktopFile => Path.Combine(AppsDir, LegacyDesktopId);
    private static string DesktopFile => File.Exists(LegacyDesktopFile) ? LegacyDesktopFile : Path.Combine(AppsDir, DesktopId);
    private static string IconDir => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps");
    private static string IconFile => Path.Combine(IconDir, "nettle.png");

    /// <summary>The file to launch: the AppImage itself (set by the AppImage runtime), else this executable.</summary>
    private static string? AppImagePath => Environment.GetEnvironmentVariable("APPIMAGE");
    private static string LaunchPath => AppImagePath ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Nettle");

    public bool IsInMenu => File.Exists(DesktopFile);
    public bool OfferOnFirstRun => !string.IsNullOrEmpty(AppImagePath);

    public void AddToMenu()
    {
        Directory.CreateDirectory(AppsDir);
        WriteIcon();
        WriteAtomic(DesktopFile, BuildEntry(LaunchPath));
        RefreshMenuCache();
        Nettle.Core.Services.Log.Info("added to the applications menu");
    }

    public void RemoveFromMenu()
    {
        try { File.Delete(DesktopFile); } catch { }
        try { File.Delete(IconFile); } catch { }
        try { File.Delete(Path.Combine(IconDir, LegacyIconName)); } catch { }
        RefreshMenuCache();
        Nettle.Core.Services.Log.Info("removed from the applications menu");
    }

    /// <summary>
    /// Startup: if the user added Nettle to the menu and then moved the AppImage, follow it. Also
    /// brings an entry from an older version (name, icon) up to date.
    /// </summary>
    public void RepairIfMoved()
    {
        try
        {
            if (!IsInMenu || AppImagePath == null) return;
            var iconChanged = WriteIcon();
            var legacyIcon = Path.Combine(IconDir, LegacyIconName);
            if (File.Exists(legacyIcon)) { File.Delete(legacyIcon); iconChanged = true; }
            var wanted = BuildEntry(AppImagePath);
            if (File.ReadAllText(DesktopFile) != wanted)
            {
                WriteAtomic(DesktopFile, wanted);
                Nettle.Core.Services.Log.Info("menu entry updated");
            }
            if (iconChanged) RefreshMenuCache();
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("menu entry repair failed: " + ex.Message); }
    }

    /// <summary>Writes the app icon unless the same one is already there. True when it was written.</summary>
    private static bool WriteIcon()
    {
        using var s = typeof(LinuxDesktopIntegration).Assembly.GetManifestResourceStream("nettle.png");
        if (s == null) return false;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var bytes = ms.ToArray();
        if (File.Exists(IconFile) && File.ReadAllBytes(IconFile).AsSpan().SequenceEqual(bytes)) return false;
        Directory.CreateDirectory(IconDir);
        File.WriteAllBytes(IconFile, bytes);
        return true;
    }

    private static string BuildEntry(string exe) =>
        new StringBuilder()
            .Append("[Desktop Entry]\n")
            .Append("Type=Application\n")
            .Append("Name=Nettle\n")
            .Append("GenericName=Jellyfin Client\n")
            .Append("Comment=Watch your Jellyfin library in original quality\n")
            .Append("Exec=").Append(QuoteExec(exe)).Append('\n')
            .Append("Icon=").Append(IconFile).Append('\n')
            .Append("Terminal=false\n")
            .Append("Categories=AudioVideo;Video;Player;TV;\n")
            .Append("Keywords=jellyfin;media;movies;tv;shows;live;\n")
            .Append("StartupWMClass=Nettle\n")
            .Append("StartupNotify=true\n")
            .ToString();

    /// <summary>
    /// Desktop Entry spec quoting: wrap in double quotes, backslash-escape " ` $ \ inside, then apply
    /// the string-value escape (\ becomes \\) on top; a literal % must be doubled.
    /// </summary>
    private static string QuoteExec(string path)
    {
        var inner = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return ("\"" + inner + "\"").Replace("\\", "\\\\").Replace("%", "%%");
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Most desktops notice new entries by themselves; this just speeds it up where available.</summary>
    private static void RefreshMenuCache()
    {
        try
        {
            var psi = new ProcessStartInfo("update-desktop-database") { UseShellExecute = false };
            psi.ArgumentList.Add(AppsDir);
            using var _ = Process.Start(psi);
        }
        catch { /* not installed: fine */ }
    }
}
