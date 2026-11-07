using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Slopfin.UI;
using Slopfin.UI.ViewModels;
using Slopfin.UI.Views;

namespace Slopfin.Desktop;

/// <summary>Borderless-feel window that extends content into the title bar for a cinematic look.</summary>
internal sealed class MainWindow : Window, IWindowHost
{
    private WindowState _restoreState = WindowState.Maximized;

    public MainWindow(MainViewModel vm)
    {
        Title = "Slopfin";
        Width = 1440;
        Height = 900;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 32;
        Background = Brushes.Black;
        Icon = LoadIcon();

        Content = new MainView { DataContext = vm };
        this[!BackgroundProperty] = this.GetResourceObservable("BgBrush").ToBinding();
        AppServices.Window = this;
    }

    /// <summary>Window + taskbar icon, from the copy embedded in this assembly.</summary>
    private static WindowIcon? LoadIcon()
    {
        try
        {
            // Windows wants the multi-size .ico; X11 window managers take a plain PNG.
            using var s = typeof(MainWindow).Assembly.GetManifestResourceStream(OperatingSystem.IsWindows() ? "slopfin.ico" : "slopfin.png");
            if (s != null) return new WindowIcon(s);
            Slopfin.Core.Services.Log.Warn("window icon resource missing");
        }
        catch (Exception ex) { Slopfin.Core.Services.Log.Error("window icon failed to load", ex); }
        return null;
    }

    public HdrDisplayState GetHdrState()
    {
        // Linux: libVLC 3 always tone-maps HDR to SDR there, whatever the display can do.
        if (!OperatingSystem.IsWindows()) return HdrDisplayState.Sdr;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        return hwnd == IntPtr.Zero ? HdrDisplayState.Unknown : DisplayHdr.ForWindow(hwnd);
    }

    public bool IsFullScreen => WindowState == WindowState.FullScreen;

    public void SetFullScreen(bool on)
    {
        if (on == IsFullScreen) return;
        if (on)
        {
            _restoreState = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            WindowState = _restoreState == WindowState.FullScreen ? WindowState.Normal : _restoreState;
        }
    }
}
