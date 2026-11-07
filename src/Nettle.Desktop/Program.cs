using Avalonia;
using Nettle.Core.Platform;
using Nettle.Desktop.Linux;
using Nettle.UI;

namespace Nettle.Desktop;

/// <summary>Desktop head for Windows and Linux. Platform-specific pieces are picked at startup.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles installer/updater lifecycle hooks (install, update, uninstall)
        // and exits immediately when invoked for one. No network access.
        Velopack.VelopackApp.Build().Run();

        // Data saved under the old name (Slopfin, up to 0.2.0) moves to the new folder first.
        // Build-time render commands (icons, splash, banner) leave it alone.
        if (!args.Any(a => a.StartsWith("--render-", StringComparison.Ordinal)))
        {
            if (OperatingSystem.IsWindows()) WindowsPlatformInfo.MoveLegacyData(); else LinuxPlatformInfo.MoveLegacyData();
        }
        IPlatformInfo platform = OperatingSystem.IsWindows() ? new WindowsPlatformInfo() : new LinuxPlatformInfo();
        Directory.CreateDirectory(platform.DataDirectory);
        var crashLog = Path.Combine(platform.DataDirectory, "crash.log");
        // Kept small: the older half is rolled to crash.log.old once it passes 1 MB.
        try { if (new FileInfo(crashLog) is { Exists: true, Length: > 1_000_000 }) File.Move(crashLog, crashLog + ".old", overwrite: true); } catch { }
        // Crash details are redacted the same way as the diagnostic log (no tokens / API keys).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.AppendAllText(crashLog, Nettle.Core.Services.Log.Redact($"[{DateTime.Now:u}] {e.ExceptionObject}\n\n"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(crashLog, Nettle.Core.Services.Log.Redact($"[{DateTime.Now:u}] (unobserved) {e.Exception}\n\n"));
            e.SetObserved();
        };

        Nettle.Core.Services.Log.Init(platform.DataDirectory);
        foreach (var note in LegacyData.Notes) Nettle.Core.Services.Log.Info(note);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Nettle.Core.Services.Log.Error("Unhandled", e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Nettle.Core.Services.Log.Info($"Process exit (code {Environment.ExitCode})");

        // libmpv: Windows uses libmpv-2.dll next to the app; Linux has its own loader.
        if (OperatingSystem.IsWindows()) Nettle.UI.Playback.PlaybackEngine.PlatformOptions = WindowsMpvOptions;
        else LinuxMpv.Initialize();

        // CI / diagnostics: `Nettle --selftest file...` decodes test files and exits.
        var selfTest = Array.IndexOf(args, "--selftest");
        if (selfTest >= 0) return SelfTest.Run(args.Skip(selfTest + 1));

        ISecureStore secure = OperatingSystem.IsWindows()
            ? new DpapiSecureStore(Path.Combine(platform.DataDirectory, "secure"))
            : new LinuxSecureStore(Path.Combine(platform.DataDirectory, "secure"));
        AppServices.Initialize(platform, secure);
        App.CreateDesktopWindow = vm => new MainWindow(vm);
        // Build-time: `Nettle --render-splash out.png` draws the installer splash art and exits.
        var splashArg = Array.IndexOf(args, "--render-splash");
        if (splashArg >= 0 && splashArg + 1 < args.Length) App.RenderSplashPath = Path.GetFullPath(args[splashArg + 1]);
        var bannerArg = Array.IndexOf(args, "--render-banner");
        if (bannerArg >= 0 && bannerArg + 1 < args.Length) App.RenderBannerPath = Path.GetFullPath(args[bannerArg + 1]);
        var iconArg = Array.IndexOf(args, "--render-icon");
        if (iconArg >= 0 && iconArg + 1 < args.Length) App.RenderIconFolder = Path.GetFullPath(args[iconArg + 1]);
        AppServices.Updater = new VelopackUpdater();
        AppServices.KeepAwake = OperatingSystem.IsWindows() ? new WindowsKeepAwake() : new LinuxKeepAwake();

        if (OperatingSystem.IsLinux())
        {
            var desktop = new LinuxDesktopIntegration();
            // Scriptable too: `Nettle --add-to-menu` / `--remove-from-menu`, then exit.
            if (args.Contains("--add-to-menu")) { desktop.AddToMenu(); return 0; }
            if (args.Contains("--remove-from-menu")) { desktop.RemoveFromMenu(); return 0; }
            desktop.RepairIfMoved();
            AppServices.Desktop = desktop;
        }

        // One copy per user (build-time render modes are exempt).
        SingleInstance? instance = null;
        if (!App.IsRenderMode)
        {
            instance = SingleInstance.TryAcquire(platform.DataDirectory);
            if (instance == null)
            {
                Nettle.Core.Services.Log.Info("already running: asking the open copy to come forward");
                SingleInstance.SignalExisting(platform.DataDirectory);
                return 0;
            }
            instance.Listen(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w })
                {
                    if (w.WindowState == Avalonia.Controls.WindowState.Minimized) w.WindowState = Avalonia.Controls.WindowState.Normal;
                    w.Activate();
                }
                AppServices.Main?.ActivatedByOtherInstance();
            }));
        }

        try
        {
            using var _ = instance;
            var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Nettle.Core.Services.Log.Info($"Lifetime ended (code {code})");
            return code;
        }
        catch (Exception ex)
        {
            Nettle.Core.Services.Log.Error("Fatal", ex);
            File.AppendAllText(crashLog, Nettle.Core.Services.Log.Redact($"[{DateTime.Now:u}] {ex}\n\n"));
            throw;
        }
    }

    /// <summary>
    /// Windows: Direct3D 11 output. target-colorspace-hint tells Windows the video's real
    /// colour space, so HDR10 / HLG / Dolby Vision go out as true HDR when Windows HDR is on.
    /// </summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> WindowsMpvOptions =
    [
        new("gpu-api", "d3d11"),
        new("gpu-context", "d3d11"),
        new("target-colorspace-hint", "yes"),
    ];

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface } })
            // Linux: WM_CLASS matches StartupWMClass in the menu entry, so docks group the window with its launcher.
            // UseDBusMenu off: Nettle has no menu bar, and on desktops without a global-menu
            // service (GNOME) the lookup only produced a D-Bus error in crash.log.
            // EGL first: mpv's render API can only use VA-API hardware decoding with an EGL context.
            .With(new X11PlatformOptions
            {
                WmClass = "Nettle",
                UseDBusMenu = false,
                RenderingMode = Nettle.UI.Playback.PlaybackEngine.UseRenderApi
                    ? [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software]
                    : [X11RenderingMode.Glx, X11RenderingMode.Software],
            })
            .LogToTrace();
}
