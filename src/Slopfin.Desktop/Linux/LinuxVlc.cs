using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Slopfin.Desktop.Linux;

/// <summary>
/// Loads libVLC on Linux. Release builds (AppImage) carry their own libVLC in "vlc/":
///   vlc/libvlc.so.5, vlc/libvlccore.so.9   libVLC itself
///   vlc/vlc/plugins/                       its plugins (libVLC finds them next to libvlccore)
///   vlc/fallback/                          libraries the plugins need (FFmpeg, etc.)
/// For each fallback library, the system's own copy is preferred when it has one (so GPU
/// drivers, TLS certificates and audio servers match the distro), and the bundled copy is
/// only used when the system lacks it. Source builds use the system's VLC instead.
/// </summary>
internal static class LinuxVlc
{
    public static void Initialize()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "vlc");
        var libvlcPath = Path.Combine(dir, "libvlc.so.5");
        IntPtr libvlc, libvlccore = IntPtr.Zero;

        if (File.Exists(libvlcPath))
        {
            PreloadFallbacks(Path.Combine(dir, "fallback"));
            libvlccore = NativeLibrary.Load(Path.Combine(dir, "libvlccore.so.9"));
            libvlc = NativeLibrary.Load(libvlcPath);
            Slopfin.Core.Services.Log.Info("libVLC: bundled copy");
        }
        else if (NativeLibrary.TryLoad("libvlc.so.5", out libvlc))
        {
            Slopfin.Core.Services.Log.Info("libVLC: system copy");
        }
        else
        {
            throw new DllNotFoundException("VLC wasn't found. Install VLC (e.g. 'sudo apt install vlc') or use the Slopfin AppImage, which includes it.");
        }

        // LibVLCSharp imports "libvlc"; point it at the exact library loaded above.
        NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, (name, _, _) => name switch
        {
            "libvlc" => libvlc,
            "libvlccore" => libvlccore,
            _ => IntPtr.Zero,
        });
        LibVLCSharp.Shared.Core.Initialize();
    }

    private static void PreloadFallbacks(string fallbackDir)
    {
        if (!Directory.Exists(fallbackDir)) return;
        var pending = Directory.GetFiles(fallbackDir, "*.so*").ToList();
        int fromSystem = 0, bundled = 0;

        // 1) System copies first (dlopen by soname searches the distro's library paths, not ours).
        pending.RemoveAll(f =>
        {
            if (!NativeLibrary.TryLoad(Path.GetFileName(f), out _)) return false;
            fromSystem++;
            return true;
        });

        // 2) Bundled copies for the rest. A library can only load once its own dependencies are
        //    loaded, so repeat until no more progress (cheap: a few passes over ~100 files).
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

        Slopfin.Core.Services.Log.Info($"libVLC dependencies: {fromSystem} from the system, {bundled} bundled, {pending.Count} unavailable"
            + (pending.Count > 0 ? " (" + string.Join(", ", pending.Select(Path.GetFileName)) + ")" : ""));
    }

    /// <summary>
    /// `Slopfin --selftest file...`: decodes each file for a few seconds without any window or
    /// sound device and reports what libVLC managed. Used by CI to prove the bundled VLC works
    /// on a clean machine. Returns the process exit code.
    /// </summary>
    public static int SelfTest(IEnumerable<string> files)
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--no-video-title-show", "--no-metadata-network-access", "--no-lua");
        Console.WriteLine($"libVLC {vlc.Version} ({vlc.Changeset})");
        Console.WriteLine("audio outputs: " + string.Join(", ", vlc.AudioOutputs.Select(a => a.Name)));
        int failures = 0;
        foreach (var file in files)
        {
            using var media = new Media(vlc, Path.GetFullPath(file), FromType.FromPath);
            using var mp = new MediaPlayer(media);
            mp.Play();
            var until = DateTime.UtcNow.AddSeconds(15);
            MediaStats stats = default;
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(250);
                stats = media.Statistics;
                if (stats.DecodedVideo > 25 && stats.DecodedAudio > 25) break;
            }
            uint w = 0, h = 0;
            mp.Size(0, ref w, ref h);
            mp.Stop();
            bool ok = stats.DecodedVideo > 25 && stats.DecodedAudio > 25;
            if (!ok) failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {Path.GetFileName(file)}: {w}x{h}, video frames {stats.DecodedVideo}, audio blocks {stats.DecodedAudio}");
        }
        return failures == 0 ? 0 : 1;
    }
}
