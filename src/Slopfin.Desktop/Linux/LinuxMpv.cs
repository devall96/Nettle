using System.Runtime.InteropServices;
using Slopfin.UI.Playback;

namespace Slopfin.Desktop.Linux;

/// <summary>
/// Loads libmpv on Linux. Release builds (AppImage) carry their own copy in "mpv/":
///   mpv/libmpv.so.2     libmpv itself
///   mpv/fallback/       libraries it needs (FFmpeg, libplacebo, ...)
///   mpv/licenses/       their copyright files and source package list
/// For each fallback library, the system's own copy is preferred when it has one (so GPU
/// drivers, TLS certificates and the audio server match the distro), and the bundled copy is
/// only used when the system lacks it. Source builds use the system's libmpv instead.
/// </summary>
internal static class LinuxMpv
{
    public static void Initialize()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "mpv");
        var bundled = Path.Combine(dir, "libmpv.so.2");
        IntPtr lib;
        if (File.Exists(bundled))
        {
            var missing = PreloadFallbacks(Path.Combine(dir, "fallback"));
            if (!NativeLibrary.TryLoad(bundled, out lib))
                throw new DllNotFoundException("The bundled libmpv couldn't be loaded" +
                    (missing.Count > 0 ? ". These libraries couldn't be loaded on this system: " + string.Join(", ", missing) : "."));
            Slopfin.Core.Services.Log.Info("libmpv: bundled copy");
        }
        else if (NativeLibrary.TryLoad("libmpv.so.2", out lib))
        {
            Slopfin.Core.Services.Log.Info("libmpv: system copy");
        }
        else
        {
            throw new DllNotFoundException("libmpv wasn't found. Install it (e.g. 'sudo apt install libmpv2') or use the Slopfin AppImage, which includes it.");
        }
        PlaybackEngine.UseLibrary(lib);
        PlaybackEngine.PlatformOptions = Options;
    }

    /// <summary>
    /// Slopfin's window is an X11 window (also under Wayland, through XWayland), so mpv must
    /// draw with an X11 context; left to itself it would pick Wayland and open its own window.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> Options =
    [
        new("gpu-api", "opengl"),
        new("gpu-context", "x11egl"),
    ];

    /// <summary>Returns the names of libraries that couldn't be loaded at all.</summary>
    private static List<string> PreloadFallbacks(string fallbackDir)
    {
        if (!Directory.Exists(fallbackDir)) return [];
        var pending = Directory.GetFiles(fallbackDir, "*.so*").ToList();
        int fromSystem = 0, bundled = 0;

        // 1) System copies first (dlopen by soname searches the distro's library paths, not ours).
        pending.RemoveAll(f =>
        {
            if (!NativeLibrary.TryLoad(Path.GetFileName(f), out _)) return false;
            fromSystem++;
            return true;
        });

        // 2) Bundled copies for the rest. A library only loads once its own dependencies are
        //    loaded, so repeat until no more progress.
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            pending.RemoveAll(f =>
            {
                if (!NativeLibrary.TryLoad(f, out _)) return false;
                bundled++;
                progress = true;
                return true;
            });
        }

        Slopfin.Core.Services.Log.Info($"libmpv dependencies: {fromSystem} from the system, {bundled} bundled, {pending.Count} unavailable"
            + (pending.Count > 0 ? " (" + string.Join(", ", pending.Select(Path.GetFileName)) + ")" : ""));
        return pending.Select(p => Path.GetFileName(p)!).ToList();
    }
}
