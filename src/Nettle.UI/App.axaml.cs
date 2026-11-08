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

    /// <summary>Build-time mode: render the installer splash art to this path, then exit.</summary>
    public static string? RenderSplashPath { get; set; }
    /// <summary>Build-time mode: render the GitHub README banner to this path, then exit.</summary>
    public static string? RenderBannerPath { get; set; }
    /// <summary>Build-time mode: render the app icon files (nettle.png / nettle.ico) into this folder, then exit.</summary>
    public static string? RenderIconFolder { get; set; }

    public static bool IsRenderMode => RenderSplashPath != null || RenderBannerPath != null || RenderIconFolder != null;

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
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
            Environment.Exit(code);
            return;
        }

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
