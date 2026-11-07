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
            var corePath = Path.Combine(dir, "libvlccore.so.9");
            PreloadFallbacks(Path.Combine(dir, "fallback"), corePath);
            libvlccore = NativeLibrary.Load(corePath);
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
        Slopfin.UI.Playback.PlaybackEngine.PlatformOptions = Options;
    }

    /// <summary>
    /// --glconv=none: libVLC 3's zero-copy VA-API → OpenGL path draws garbled stripes with newer
    /// Mesa drivers (seen on AMD RDNA 4). Without it, the GPU still decodes, and frames are copied
    /// back once before OpenGL draws them: correct picture on every driver, small CPU cost.
    /// </summary>
    public static readonly string[] Options = ["--glconv=none"];

    private static void PreloadFallbacks(string fallbackDir, string corePath)
    {
        if (!Directory.Exists(fallbackDir)) return;
        var pending = Directory.GetFiles(fallbackDir, "*.so*").ToList();
        int fromSystem = 0, bundled = 0;

        // 1) System copies first (dlopen by soname searches the distro's library paths, not ours).
        //    VLC's own helper libraries (libvlc_pulse, libvlc_xcb_events...) always come from the
        //    bundle: they must match our libvlccore exactly.
        pending.RemoveAll(f =>
        {
            if (Path.GetFileName(f).StartsWith("libvlc", StringComparison.Ordinal)) return false;
            if (!NativeLibrary.TryLoad(Path.GetFileName(f), out _)) return false;
            fromSystem++;
            return true;
        });

        // 2) Bundled copies for the rest, plus libvlccore itself (VLC's helper libraries link
        //    against it). A library can only load once its own dependencies are loaded, so
        //    repeat until no more progress (cheap: a few passes over ~100 files).
        pending.Add(corePath);
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

        if (!pending.Remove(corePath)) bundled--; // don't count libvlccore as a dependency
        Slopfin.Core.Services.Log.Info($"libVLC dependencies: {fromSystem} from the system, {bundled} bundled, {pending.Count} unavailable"
            + (pending.Count > 0 ? " (" + string.Join(", ", pending.Select(Path.GetFileName)) + ")" : ""));
    }

    /// <summary>
    /// `Slopfin --selftest [--window] [--hw] [--snap DIR] file...`: decodes each file for a few
    /// seconds and reports what libVLC managed. By default there's no window or sound device, so
    /// CI can prove the bundled VLC decodes on a clean machine. --window uses real video/audio
    /// outputs, --hw allows hardware decoding, --snap saves a frame of each file (needs --window).
    /// Returns the process exit code.
    /// </summary>
    public static int SelfTest(IEnumerable<string> args)
    {
        var list = args.ToList();
        bool window = list.Remove("--window");
        bool hw = list.Remove("--hw");
        string? snapDir = null;
        int snapAt = list.IndexOf("--snap");
        if (snapAt >= 0 && snapAt + 1 < list.Count) { snapDir = Path.GetFullPath(list[snapAt + 1]); list.RemoveRange(snapAt, 2); Directory.CreateDirectory(snapDir); }

        var options = new List<string>(list.Remove("--raw") ? [] : Options) { "--no-video-title-show", "--no-metadata-network-access", "--no-lua" };
        if (!window) { options.Add("--vout=dummy"); options.Add("--aout=dummy"); }
        // --vlcopt=--some-vlc-option passes raw libVLC options through (diagnostics).
        options.AddRange(list.Where(a => a.StartsWith("--vlcopt=")).Select(a => a["--vlcopt=".Length..]));
        list.RemoveAll(a => a.StartsWith("--vlcopt="));
        var holdSeconds = list.Remove("--hold") ? 8 : 0;
        using var vlc = new LibVLC(options.ToArray());
        vlc.Log += (_, e) => { if (e.Level >= LogLevel.Warning) Console.WriteLine($"  vlc {e.Level.ToString().ToLowerInvariant()} [{e.Module}] {e.Message}"); };
        Console.WriteLine($"libVLC {vlc.Version} ({vlc.Changeset})");
        Console.WriteLine("audio outputs: " + string.Join(", ", vlc.AudioOutputs.Select(a => a.Name)));
        int failures = 0;
        foreach (var file in list)
        {
            using var media = file.Contains("://")
                ? new Media(vlc, new Uri(file))
                : new Media(vlc, Path.GetFullPath(file), FromType.FromPath);
            using var mp = new MediaPlayer(media) { EnableHardwareDecoding = hw };
            mp.Play();
            var until = DateTime.UtcNow.AddSeconds(15);
            MediaStats stats = default;
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(250);
                stats = media.Statistics;
                if (stats.DecodedVideo > 25 && stats.DecodedAudio > 25) break;
            }
            if (holdSeconds > 0) { Console.WriteLine("  holding"); Thread.Sleep(holdSeconds * 1000); }
            if (snapDir != null)
            {
                Thread.Sleep(1500);
                var shot = Path.Combine(snapDir, Path.GetFileNameWithoutExtension(file) + (hw ? "-hw" : "-sw") + ".png");
                Console.WriteLine(mp.TakeSnapshot(0, shot, 0, 0) ? $"  snapshot: {shot}" : "  snapshot failed");
                Thread.Sleep(500);
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
