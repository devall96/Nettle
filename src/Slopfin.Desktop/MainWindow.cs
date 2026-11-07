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
    private readonly MainViewModel _vm;
    private bool _readyToClose;
    // Last size/position while in a normal (not maximized / full-screen) state.
    private Size _normalSize;
    private PixelPoint? _normalPos;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "Slopfin";
        Width = 1440;
        Height = 900;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RestorePlacement();
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 32;
        Background = Brushes.Black;
        Icon = LoadIcon();

        Content = new MainView { DataContext = vm };
        this[!BackgroundProperty] = this.GetResourceObservable("BgBrush").ToBinding();
        AppServices.Window = this;
        PositionChanged += (_, _) => { if (WindowState == WindowState.Normal) _normalPos = Position; };
    }

    // ---------- Remember the window's size and place ----------

    private void RestorePlacement()
    {
        var p = AppServices.Settings.Current.Window;
        if (p == null || p.Width < MinWidth || p.Height < MinHeight) return;
        Width = p.Width;
        Height = p.Height;
        _normalSize = new Size(p.Width, p.Height);
        if (p.X is { } x && p.Y is { } y)
        {
            // Only reuse the position if it's still on a connected screen (monitors change).
            Opened += (_, _) =>
            {
                var pos = new PixelPoint(x, y);
                if (Screens.All.Any(s => s.WorkingArea.Contains(pos + new PixelPoint(40, 40))))
                {
                    Position = pos;
                    _normalPos = pos;
                }
            };
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var size = _normalSize.Width > 0 ? _normalSize : ClientSize;
        AppServices.Settings.Current.Window = new Slopfin.Core.Models.WindowPlacement
        {
            Width = Math.Round(size.Width),
            Height = Math.Round(size.Height),
            X = _normalPos?.X,
            Y = _normalPos?.Y,
            Maximized = WindowState == WindowState.Maximized || (_restoreState == WindowState.Maximized && IsFullScreen),
        };
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_readyToClose || e.Cancel) return;
        // Finish the "stopped" report (resume position) and save settings before exiting.
        e.Cancel = true;
        _readyToClose = true;
        try
        {
            SavePlacement();
            await _vm.ShutdownAsync();
            await AppServices.Settings.SaveAsync();
        }
        catch (Exception ex) { Slopfin.Core.Services.Log.Error("shutdown", ex); }
        Close();
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
        // Linux: Slopfin's window goes through XWayland/X11, which has no HDR path, so mpv tone-maps to SDR.
        if (!OperatingSystem.IsWindows()) return HdrDisplayState.Sdr;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        return hwnd == IntPtr.Zero ? HdrDisplayState.Unknown : DisplayHdr.ForWindow(hwnd);
    }

    // ---------- Linux: keep the player's overlay window lined up ----------
    // The player's controls live in a separate transparent window over the video (that's how
    // UI goes above native video, see VideoSurface). Desktop shells keep ordinary windows out of the
    // dock / top-bar area, so in full screen GNOME pushed that overlay right and down by the dock
    // and bar size, cutting the controls off at the right edge. Fix: while the main window is
    // full screen, the overlay is made full screen too (shells allow that), and restored after.

    private Avalonia.Threading.DispatcherTimer? _linuxSync;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty && WindowState == WindowState.Normal && !IsFullScreen)
            _normalSize = ClientSize;
        if (!OperatingSystem.IsLinux()) return;
        if (change.Property == WindowStateProperty || change.Property == ClientSizeProperty)
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(SyncLinuxWindows, TimeSpan.FromMilliseconds(120));
            Avalonia.Threading.DispatcherTimer.RunOnce(SyncLinuxWindows, TimeSpan.FromMilliseconds(600));
        }
        // Overlays appear and window states change from outside (keyboard shortcuts, the shell),
        // so keep a cheap periodic check running while any overlay is around.
        _linuxSync ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(700), Avalonia.Threading.DispatcherPriority.Background, (_, _) => SyncLinuxWindows());
        if (!_linuxSync.IsEnabled) _linuxSync.Start();
    }

    /// <summary>
    /// True when the window really covers its whole screen. Avalonia reports "Maximized" for a
    /// window that is both maximized and full screen (GNOME keeps both flags), so the state
    /// alone can't be trusted on Linux.
    /// </summary>
    private bool CoversScreen()
    {
        if (WindowState == WindowState.FullScreen) return true;
        var screen = Screens.ScreenFromWindow(this);
        if (screen == null) return false;
        var size = PixelSize.FromSize(ClientSize, RenderScaling);
        return size.Width >= screen.Bounds.Width && size.Height >= screen.Bounds.Height;
    }

    private void SyncLinuxWindows()
    {
        var wanted = CoversScreen() ? WindowState.FullScreen : WindowState.Normal;
        foreach (var overlay in OwnedWindows)
        {
            if (!overlay.IsVisible) continue;
            // Window managers refuse to make
            // fixed-size windows full screen, so allow resizing first (it has no frame anyway).
            if (wanted == WindowState.FullScreen && !overlay.CanResize) overlay.CanResize = true;
            if (overlay.WindowState != wanted) overlay.WindowState = wanted;
        }
    }

    public bool IsFullScreen => WindowState == WindowState.FullScreen || (OperatingSystem.IsLinux() && CoversScreen());

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
            // Linux: a window can be full screen while Avalonia reports "Maximized"; pass through
            // FullScreen first so the change back actually clears the full-screen flag.
            if (WindowState != WindowState.FullScreen) WindowState = WindowState.FullScreen;
            WindowState = _restoreState == WindowState.FullScreen ? WindowState.Normal : _restoreState;
        }
    }
}
