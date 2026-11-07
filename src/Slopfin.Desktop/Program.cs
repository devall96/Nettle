using Avalonia;
using Slopfin.Core.Platform;
using Slopfin.Desktop.Linux;
using Slopfin.UI;

namespace Slopfin.Desktop;

/// <summary>Desktop head for Windows and Linux. Platform-specific pieces are picked at startup.</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: handles installer/updater lifecycle hooks (install, update, uninstall)
        // and exits immediately when invoked for one. No network access.
        Velopack.VelopackApp.Build().Run();

        IPlatformInfo platform = OperatingSystem.IsWindows() ? new WindowsPlatformInfo() : new LinuxPlatformInfo();
        Directory.CreateDirectory(platform.DataDirectory);
        var crashLog = Path.Combine(platform.DataDirectory, "crash.log");
        // Crash details are redacted the same way as the diagnostic log (no tokens / API keys).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.AppendAllText(crashLog, Slopfin.Core.Services.Log.Redact($"[{DateTime.Now:u}] {e.ExceptionObject}\n\n"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(crashLog, Slopfin.Core.Services.Log.Redact($"[{DateTime.Now:u}] (unobserved) {e.Exception}\n\n"));
            e.SetObserved();
        };

        Slopfin.Core.Services.Log.Init(platform.DataDirectory);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Slopfin.Core.Services.Log.Error("Unhandled", e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Slopfin.Core.Services.Log.Info($"Process exit (code {Environment.ExitCode})");

        // libVLC: Windows uses the bundled VideoLAN.LibVLC.Windows package; Linux has its own loader.
        if (OperatingSystem.IsWindows()) LibVLCSharp.Shared.Core.Initialize();
        else LinuxVlc.Initialize();

        // CI / diagnostics: `Slopfin --selftest file...` decodes test files headlessly and exits.
        var selfTest = Array.IndexOf(args, "--selftest");
        if (selfTest >= 0) return LinuxVlc.SelfTest(args.Skip(selfTest + 1));

        ISecureStore secure = OperatingSystem.IsWindows()
            ? new DpapiSecureStore(Path.Combine(platform.DataDirectory, "secure"))
            : new LinuxSecureStore(Path.Combine(platform.DataDirectory, "secure"));
        AppServices.Initialize(platform, secure);
        App.CreateDesktopWindow = vm => new MainWindow(vm);
        // Build-time: `Slopfin --render-splash out.png` draws the installer splash art and exits.
        var splashArg = Array.IndexOf(args, "--render-splash");
        if (splashArg >= 0 && splashArg + 1 < args.Length) App.RenderSplashPath = Path.GetFullPath(args[splashArg + 1]);
        var bannerArg = Array.IndexOf(args, "--render-banner");
        if (bannerArg >= 0 && bannerArg + 1 < args.Length) App.RenderBannerPath = Path.GetFullPath(args[bannerArg + 1]);
        AppServices.Updater = new VelopackUpdater();

        if (OperatingSystem.IsLinux())
        {
            var desktop = new LinuxDesktopIntegration();
            // Scriptable too: `Slopfin --add-to-menu` / `--remove-from-menu`, then exit.
            if (args.Contains("--add-to-menu")) { desktop.AddToMenu(); return 0; }
            if (args.Contains("--remove-from-menu")) { desktop.RemoveFromMenu(); return 0; }
            desktop.RepairIfMoved();
            AppServices.Desktop = desktop;
        }

        try
        {
            var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Slopfin.Core.Services.Log.Info($"Lifetime ended (code {code})");
            return code;
        }
        catch (Exception ex)
        {
            Slopfin.Core.Services.Log.Error("Fatal", ex);
            File.AppendAllText(crashLog, Slopfin.Core.Services.Log.Redact($"[{DateTime.Now:u}] {ex}\n\n"));
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions { CompositionMode = new[] { Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface } })
            // Linux: WM_CLASS matches StartupWMClass in the menu entry, so docks group the window with its launcher.
            // UseDBusMenu off: Slopfin has no menu bar, and on desktops without a global-menu
            // service (GNOME) the lookup only produced a D-Bus error in crash.log.
            .With(new X11PlatformOptions { WmClass = "Slopfin", UseDBusMenu = false })
            .LogToTrace();
}
