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

    // ---------- Linux: keep the player's overlay window lined up ----------
    // The player's controls live in a separate transparent window over the video (that's how
    // LibVLCSharp draws UI above native video). Desktop shells keep ordinary windows out of the
    // dock / top-bar area, so in full screen GNOME pushed that overlay right and down by the dock
    // and bar size, cutting the controls off at the right edge. Fix: while the main window is
    // full screen, the overlay is made full screen too (shells allow that), and restored after.
    // Also, if the shell moved our window without telling Avalonia, re-sync its position.

    private Avalonia.Threading.DispatcherTimer? _linuxSync;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!OperatingSystem.IsLinux()) return;
        if (change.Property == WindowStateProperty || change.Property == ClientSizeProperty)
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(SyncLinuxWindows, TimeSpan.FromMilliseconds(120));
            Avalonia.Threading.DispatcherTimer.RunOnce(SyncLinuxWindows, TimeSpan.FromMilliseconds(600));
        }
        if (change.Property == WindowStateProperty)
        {
            // The overlay can appear after the state change (player opening), so keep checking while full screen.
            _linuxSync ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(700), Avalonia.Threading.DispatcherPriority.Background, (_, _) => SyncLinuxWindows());
            if (IsFullScreen) _linuxSync.Start(); else _linuxSync.Stop();
        }
    }

    private void SyncLinuxWindows()
    {
        var handle = TryGetPlatformHandle();
        if (handle?.HandleDescriptor == "XID" && Linux.X11.TryGetRootPosition(handle.Handle) is { } real && real != Position)
        {
            Slopfin.Core.Services.Log.Info($"window position resync {Position} -> {real}");
            Position = real;
        }

        var wanted = IsFullScreen ? WindowState.FullScreen : WindowState.Normal;
        foreach (var overlay in OwnedWindows)
        {
            if (!overlay.IsVisible) continue;
            // LibVLCSharp creates the overlay fixed-size; window managers refuse to make
            // fixed-size windows full screen, so allow resizing first (it has no frame anyway).
            if (wanted == WindowState.FullScreen && !overlay.CanResize) overlay.CanResize = true;
            if (overlay.WindowState != wanted) overlay.WindowState = wanted;
        }
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
