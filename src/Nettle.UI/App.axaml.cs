using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nettle.UI.Playback;
using Nettle.UI.Theming;
using Nettle.UI.ViewModels;
using Nettle.UI.Views;

namespace Nettle.UI;

public partial class App : Application
{
    /// <summary>Set by the platform head to create its top-level window (desktop) or view (mobile/TV).</summary>
    public static Func<MainViewModel, object>? CreateDesktopWindow { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static readonly List<DateTime> _uiErrors = new();

    /// <summary>Build-time mode: render the installer splash art to this path, then exit.</summary>
    public static string? RenderSplashPath { get; set; }
    /// <summary>Build-time mode: render the GitHub README banner to this path, then exit.</summary>
    public static string? RenderBannerPath { get; set; }
    /// <summary>Build-time mode: render the app icon files (nettle.png / nettle.ico) into this folder, then exit.</summary>
    public static string? RenderIconFolder { get; set; }
    /// <summary>Build-time mode: render Android TV's launcher icons and home banner into this res folder, then exit.</summary>
    public static string? RenderAndroidResFolder { get; set; }

    /// <summary>Build-time mode: render the Microsoft Store package's logos into this folder, then exit.</summary>
    public static string? RenderStoreFolder { get; set; }

    public static bool IsRenderMode => RenderSplashPath != null || RenderBannerPath != null || RenderIconFolder != null || RenderAndroidResFolder != null || RenderStoreFolder != null;

    public override void OnFrameworkInitializationCompleted()
    {
        if (IsRenderMode)
        {
            int code = 0;
            try
            {
                if (RenderSplashPath != null) InstallerSplash.RenderTo(RenderSplashPath, AppServices.Version);
                if (RenderBannerPath != null) InstallerSplash.RenderBanner(RenderBannerPath);
                if (RenderIconFolder != null) AppIcon.RenderFiles(RenderIconFolder);
                if (RenderAndroidResFolder != null) AppIcon.RenderAndroidFiles(RenderAndroidResFolder);
                if (RenderStoreFolder != null) AppIcon.RenderStoreFiles(RenderStoreFolder);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            Environment.Exit(code);
            return;
        }

        // An unexpected error in one button or page used to close the whole app (and the video with it). Log it,
        // say so, and carry on; only a storm of them (something really broken) still ends the app.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Nettle.Core.Services.Log.Error("UI error (Nettle kept running)", e.Exception);
            var now = DateTime.UtcNow;
            _uiErrors.RemoveAll(t => now - t > TimeSpan.FromSeconds(10));
            _uiErrors.Add(now);
            if (_uiErrors.Count > 5) return; // not handled: the app closes and crash.log gets the details
            e.Handled = true;
            AppServices.Main?.Toast("Something went wrong: " + e.Exception.Message, 5000);
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var theme = AppServices.Settings.Current.Theme;
#if DEBUG || NETTLE_DEV
        // Developer hook: NETTLE_DEV_THEME=<key> starts in that theme without saving it.
        theme = Environment.GetEnvironmentVariable("NETTLE_DEV_THEME") ?? theme;
#endif
        ThemeManager.Apply(theme);
        Nettle.Core.Services.Log.Info($"startup: theme {sw.ElapsedMilliseconds} ms");
        // TV: 10-foot tweaks (focus, scrolling, no scroll bars), see Styles/Tv.axaml.
        if (AppServices.Profile.IsTv)
            Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Nettle.UI/"))
            {
                Source = new Uri("avares://Nettle.UI/Styles/Tv.axaml"),
            });
        var vm = new MainViewModel();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && CreateDesktopWindow != null)
        {
            desktop.MainWindow = (Avalonia.Controls.Window)CreateDesktopWindow(vm);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView { DataContext = vm };
            Nettle.Core.Services.Log.Info($"startup: main view {sw.ElapsedMilliseconds} ms");
        }

        PlaybackEngine.Prewarm();
        _ = vm.StartAsync();
        base.OnFrameworkInitializationCompleted();
    }
}
