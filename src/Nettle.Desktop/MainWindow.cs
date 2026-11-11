using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Nettle.Core.Models;
using Nettle.UI;
using Nettle.UI.ViewModels;
using Nettle.UI.Views;

namespace Nettle.Desktop;

/// <summary>
/// The desktop window, in one of three display modes (Settings → Display mode):
/// windowed (a real window with the system title bar and borders, any size, the UI scaling
/// down to fit), borderless full screen, or exclusive full screen.
/// </summary>
internal sealed class MainWindow : Window, IWindowHost
{
    private WindowState _restoreState = WindowState.Normal;
    private readonly MainViewModel _vm;
    private bool _readyToClose;
    // Last size/position while in a normal (not maximized / full-screen) state.
    private Size _normalSize;
    private PixelPoint? _normalPos;

    // The layout the UI is designed for. Smaller windows shrink the whole UI to fit instead of
    // cutting it off; bigger ones just get more room.
    private const double DesignWidth = 1280, DesignHeight = 720;
    private readonly LayoutTransformControl _scaler;

    private DisplayMode _mode = DisplayMode.Windowed;
    private double? _videoFps;
    private readonly ExclusiveDisplay _exclusive = new();
    private readonly HdrSwitcher _hdr = new(AppServices.Platform.DataDirectory);

    private static Nettle.Core.Models.AppSettings S => AppServices.Settings.Current;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "Nettle";
        Width = 1440;
        Height = 900;
        // Windowed: as small as you like (the UI scales down), as big as the screen allows.
        MinWidth = 320;
        MinHeight = 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // A real system title bar and borders: dragging and resizing are handled by the OS itself.
        SystemDecorations = SystemDecorations.Full;
        CanResize = true;
        RestorePlacement();
        Background = Brushes.Black;
        Icon = LoadIcon();
        // The window / taskbar icon takes the chosen theme's colours (the files on disk use the default theme).
        SetThemedIcon(Nettle.UI.Theming.ThemeManager.Current);
        Nettle.UI.Theming.ThemeManager.Applied += SetThemedIcon;
        Closed += (_, _) => Nettle.UI.Theming.ThemeManager.Applied -= SetThemedIcon;

        _scaler = new LayoutTransformControl { Child = new MainView { DataContext = vm } };
        Content = _scaler;
        this[!BackgroundProperty] = this.GetResourceObservable("BgBrush").ToBinding();
        AppServices.Window = this;
        PositionChanged += (_, _) => { if (WindowState == WindowState.Normal && !IsFullScreen) _normalPos = Position; };

        // Open straight into the saved display mode (borderless / exclusive start full screen).
        if (S.DisplayMode != DisplayMode.Windowed)
        {
            _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _mode = S.DisplayMode;
            WindowState = WindowState.FullScreen;
        }

        Opened += (_, _) =>
        {
            // Images are fetched at the screen's real pixel size (e.g. 200% on a 4K display).
            AppServices.UiScale = RenderScaling;
            if (_mode == DisplayMode.Exclusive) EnterExclusive();
#if DEBUG || NETTLE_DEV
            // NETTLE_DEV_SHOT=<folder>@12,30,45: saves the app's own picture (UI layers; video drawn by OpenGL may be
            // missing) at those seconds after start, for test runs where the desktop can't be captured (GNOME Wayland).
            if (Environment.GetEnvironmentVariable("NETTLE_DEV_SHOT") is { } shot && shot.Split('@') is [var folder, var times])
                foreach (var t in times.Split(',')) if (int.TryParse(t, out var sec))
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => DevShot(Path.Combine(folder, $"nettle-{sec:000}s.png")), TimeSpan.FromSeconds(sec));
#endif
        };
        ScalingChanged += (_, _) => AppServices.UiScale = RenderScaling;
        // Exclusive full screen gives the display back while you're in another app.
        Activated += (_, _) => { if (_mode == DisplayMode.Exclusive) EnterExclusive(); };
        Deactivated += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(OnMaybeLeftApp, TimeSpan.FromMilliseconds(200));
        Closed += (_, _) => { _exclusive.Restore(); _hdr.Restore(); };
        if (OperatingSystem.IsWindows()) _hdr.RecoverAfterCrash();
    }

#if DEBUG || NETTLE_DEV
    private void DevShot(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var size = new PixelSize(Math.Max(1, (int)ClientSize.Width), Math.Max(1, (int)ClientSize.Height));
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
            bmp.Render(this);
            // The player's controls may live in their own window (Windows): draw those over it too.
            foreach (var w in OwnedWindows.Where(w => w.IsVisible))
            {
                using var over = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                over.Render(w);
                using var ctx = bmp.CreateDrawingContext(false);
                ctx.DrawImage(over, new Rect(0, 0, size.Width, size.Height));
            }
            bmp.Save(path);
            Nettle.Core.Services.Log.Info("dev: saved " + path);
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("dev: snapshot failed: " + ex.Message); }
    }
#endif

    // ---------- Display modes: windowed, borderless, exclusive ----------

    public bool SupportsDisplayModes => true;
    public DisplayMode DisplayMode => _mode;
    public bool IsFullScreen => _mode != DisplayMode.Windowed;

    /// <summary>F11 and the player: on = the full-screen type chosen in Settings.</summary>
    public void SetFullScreen(bool on) =>
        SetDisplayMode(on ? (S.FullScreenKind == DisplayMode.Exclusive ? DisplayMode.Exclusive : DisplayMode.Borderless) : DisplayMode.Windowed);

    public void SetDisplayMode(DisplayMode mode)
    {
        if (mode == _mode) return;
        var old = _mode;
        _mode = mode;
        Nettle.Core.Services.Log.Info($"display mode: {old} → {mode}");
        if (old == DisplayMode.Exclusive) LeaveExclusive();
        if (mode == DisplayMode.Windowed)
        {
            // Linux: a window can be full screen while Avalonia reports "Maximized"; pass through
            // FullScreen first so the change back actually clears the full-screen flag.
            if (WindowState != WindowState.FullScreen) WindowState = WindowState.FullScreen;
            WindowState = _restoreState;
        }
        else
        {
            if (old == DisplayMode.Windowed)
                _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            if (WindowState != WindowState.FullScreen) WindowState = WindowState.FullScreen;
            if (mode == DisplayMode.Exclusive) EnterExclusive();
        }
    }

    public bool SupportsHdrSwitching => OperatingSystem.IsWindows();

    public async Task<bool> SetVideoHdrAsync(bool? hdr)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (hdr != null && !S.MatchHdrToVideo) return false;
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var monitor = hwnd == IntPtr.Zero ? null : DisplayHdr.MonitorDeviceName(hwnd);
        // Switching takes Windows a moment (the screen goes dark), so it's done off the UI thread.
        return await Task.Run(() => _hdr.Apply(monitor, hdr));
    }

    public void SetVideoFrameRate(double? fps)
    {
        _videoFps = fps is > 1 ? fps : null;
        if (_mode == DisplayMode.Exclusive && IsActive) EnterExclusive();
    }

    /// <summary>
    /// Exclusive full screen: the window stays on top of everything and the monitor's refresh rate
    /// follows the video (23.976 / 24 / 25 / 50 / 59.94 Hz…, so films don't judder). Windows only
    /// for the refresh-rate part; Linux asks the compositor to step aside (unredirect) instead.
    /// </summary>
    private void EnterExclusive()
    {
        Topmost = true;
        var handle = TryGetPlatformHandle();
        if (OperatingSystem.IsWindows() && handle != null)
            _exclusive.MatchRefreshRate(handle.Handle, _videoFps);
        else if (OperatingSystem.IsLinux() && handle != null)
            LinuxCompositorHint.SetBypass(handle.Handle, true);
    }

    private void LeaveExclusive()
    {
        Topmost = false;
        _exclusive.Restore();
        if (OperatingSystem.IsLinux() && TryGetPlatformHandle() is { } handle)
            LinuxCompositorHint.SetBypass(handle.Handle, false);
    }

    /// <summary>Focus went somewhere: if it left Nettle (not just to the player-controls window), let go of the display.</summary>
    private void OnMaybeLeftApp()
    {
        if (_mode != DisplayMode.Exclusive || IsActive || OwnedWindows.Any(w => w.IsActive)) return;
        Topmost = false;
        _exclusive.Restore();
    }

    /// <summary>Shrinks the whole UI when the window is smaller than the layout it was designed for.</summary>
    private void UpdateContentScale()
    {
        var size = ClientSize;
        // Window's own constructor reports a size before this class's fields are set up.
        if (_scaler == null || size.Width < 1 || size.Height < 1) return;
        var scale = Math.Clamp(Math.Min(size.Width / DesignWidth, size.Height / DesignHeight), 0.25, 1);
        _scaler.LayoutTransform = scale < 0.999 ? new ScaleTransform(scale, scale) : null;
        AppServices.ContentScale = scale;
    }

    // ---------- Remember the window's size and place ----------

    private void RestorePlacement()
    {
        var p = AppServices.Settings.Current.Window;
        if (p == null || p.Width < MinWidth || p.Height < MinHeight) return;
        Width = p.Width;
        Height = p.Height;
        _normalSize = new Size(p.Width, p.Height);
        // The position is applied before the window is shown, so it becomes the restore position
        // underneath a maximized window. (Setting Position after a maximized window opened moved the
        // maximized window itself: it started half off screen and couldn't be dragged.)
        if (p.X is { } x && p.Y is { } y && FitOnScreen(new PixelPoint(x, y), _normalSize) is { } pos)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = pos;
            _normalPos = pos;
        }
        if (p.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// The saved position, nudged so the whole window lies inside the screen it's on; null when that
    /// screen is gone (monitors change) or the window no longer fits there.
    /// </summary>
    private PixelPoint? FitOnScreen(PixelPoint pos, Size size)
    {
        var screens = Screens?.All;
        if (screens == null || screens.Count == 0) return null;
        var screen = screens.FirstOrDefault(s => s.WorkingArea.Contains(pos + new PixelPoint(40, 40)));
        if (screen == null) return null;
        var area = screen.WorkingArea;
        var px = PixelSize.FromSize(size, screen.Scaling);
        // Leave room for the frame Windows adds around the client area.
        if (px.Width > area.Width || px.Height + 40 > area.Height) return null;
        var nx = Math.Clamp(pos.X, area.X, area.Right - px.Width);
        var ny = Math.Clamp(pos.Y, area.Y, area.Bottom - px.Height - 40);
        return new PixelPoint(nx, ny);
    }

    private void SavePlacement()
    {
        var size = _normalSize.Width > 0 ? _normalSize : ClientSize;
        AppServices.Settings.Current.Window = new Nettle.Core.Models.WindowPlacement
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
        catch (Exception ex) { Nettle.Core.Services.Log.Error("shutdown", ex); }
        Close();
    }

    private void SetThemedIcon(Nettle.UI.Theming.ThemePalette palette)
    {
        try
        {
            // Windows: a multi-size .ico so the title bar (small) and taskbar (large) both look sharp.
            using var ms = new MemoryStream(OperatingSystem.IsWindows()
                ? Nettle.UI.Views.AppIcon.RenderIco(palette, 16, 20, 24, 32, 40, 48, 64, 256)
                : PngBytes(palette));
            Icon = new WindowIcon(ms);
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("themed window icon failed: " + ex.Message); }
    }

    private static byte[] PngBytes(Nettle.UI.Theming.ThemePalette palette)
    {
        using var bmp = Nettle.UI.Views.AppIcon.Render(256, palette);
        using var ms = new MemoryStream();
        bmp.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Window + taskbar icon, from the copy embedded in this assembly.</summary>
    private static WindowIcon? LoadIcon()
    {
        try
        {
            // Windows wants the multi-size .ico; X11 window managers take a plain PNG.
            using var s = typeof(MainWindow).Assembly.GetManifestResourceStream(OperatingSystem.IsWindows() ? "nettle.ico" : "nettle.png");
            if (s != null) return new WindowIcon(s);
            Nettle.Core.Services.Log.Warn("window icon resource missing");
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Error("window icon failed to load", ex); }
        return null;
    }

    public HdrDisplayState GetHdrState()
    {
        // Linux: Nettle's window goes through XWayland/X11, which has no HDR path, so mpv tone-maps to SDR.
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
        if (change.Property == ClientSizeProperty)
        {
            if (WindowState == WindowState.Normal && !IsFullScreen) _normalSize = ClientSize;
            UpdateContentScale();
        }
        // Full screen left from outside Nettle (the window manager, a system shortcut): follow it.
        if (change.Property == WindowStateProperty && WindowState is WindowState.Normal or WindowState.Maximized && _mode != DisplayMode.Windowed
            && !(OperatingSystem.IsLinux() && CoversScreen()))
        {
            if (_mode == DisplayMode.Exclusive) LeaveExclusive();
            _mode = DisplayMode.Windowed;
        }
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

}
