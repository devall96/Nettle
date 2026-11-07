using Avalonia;
using Slopfin.UI;

namespace Slopfin.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles installer/updater lifecycle hooks (install, update, uninstall)
        // and exits immediately when invoked for one. No network access.
        Velopack.VelopackApp.Build().Run();

        var platform = new WindowsPlatformInfo();
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

        AppServices.Initialize(platform, new DpapiSecureStore(Path.Combine(platform.DataDirectory, "secure")));
        App.CreateDesktopWindow = vm => new MainWindow(vm);
        // Build-time: `Slopfin.exe --render-splash out.png` draws the installer splash art and exits.
        var splashArg = Array.IndexOf(args, "--render-splash");
        if (splashArg >= 0 && splashArg + 1 < args.Length) App.RenderSplashPath = Path.GetFullPath(args[splashArg + 1]);
        var bannerArg = Array.IndexOf(args, "--render-banner");
        if (bannerArg >= 0 && bannerArg + 1 < args.Length) App.RenderBannerPath = Path.GetFullPath(args[bannerArg + 1]);
        AppServices.Updater = new VelopackUpdater();

        // Loads libvlc.dll from the bundled VideoLAN.LibVLC.Windows package.
        LibVLCSharp.Shared.Core.Initialize();

        try
        {
            var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Slopfin.Core.Services.Log.Info($"Lifetime ended (code {code})");
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
            .LogToTrace();
}
