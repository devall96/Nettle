using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Platform;
using Nettle.UI.Playback;

namespace Nettle.UI.Controls;

/// <summary>
/// Hosts mpv's video in a native child window (HWND on Windows, X11 window on Linux) so mpv
/// can present HDR directly, and shows <see cref="Content"/> (the player controls) in a
/// transparent borderless window kept exactly over it. Native video always draws above
/// Avalonia's own content in the same window, so a separate overlay window is the only way
/// to put UI on top of it.
/// </summary>
public sealed class VideoSurface : NativeControlHost
{
    public static readonly StyledProperty<MpvPlayer?> PlayerProperty =
        AvaloniaProperty.Register<VideoSurface, MpvPlayer?>(nameof(Player));

    public static readonly StyledProperty<Control?> ContentProperty =
        AvaloniaProperty.Register<VideoSurface, Control?>(nameof(Content));

    public MpvPlayer? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }

    /// <summary>UI shown above the video (in the overlay window).</summary>
    [Content]
    public Control? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    private IPlatformHandle? _native;
    private Window? _overlay;
    private Window? _owner;

    static VideoSurface()
    {
        PlayerProperty.Changed.AddClassHandler<VideoSurface>((s, _) => s.AttachPlayer());
        ContentProperty.Changed.AddClassHandler<VideoSurface>((s, _) => { if (s._overlay != null) s._overlay.Content = s.Content; });
        IsVisibleProperty.Changed.AddClassHandler<VideoSurface>((s, _) => s.UpdateOverlay());
        BoundsProperty.Changed.AddClassHandler<VideoSurface>((s, _) => s.UpdateOverlay());
    }

    // ---------------- native video window ----------------

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _native = base.CreateNativeControlCore(parent); // a plain child window owned by Avalonia
        AttachPlayer();
        return _native;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Shut mpv down BEFORE its window disappears, so it never renders into a dead window.
        Player?.Terminate();
        _native = null;
        base.DestroyNativeControlCore(control);
    }

    private void AttachPlayer()
    {
        if (_native == null || Player == null) return;
        try { Player.Initialize(_native.Handle.ToInt64()); }
        catch (Exception ex) { Nettle.Core.Services.Log.Error("mpv failed to start", ex); }
    }

    // ---------------- overlay window ----------------

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = TopLevel.GetTopLevel(this) as Window;
        if (_owner == null) return;
        _owner.LayoutUpdated += OnOwnerMoved;
        _owner.PositionChanged += OnOwnerMoved;
        _overlay ??= new Window
        {
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            ShowInTaskbar = false,
            ShowActivated = true,
            SizeToContent = SizeToContent.Manual,
            Title = "Nettle player controls",
            Content = Content,
        };
        // Window managers can resize/move the overlay themselves (e.g. restoring its old size when
        // leaving full screen). Re-check whenever that happens.
        _overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.ClientSizeProperty || e.Property == Window.WindowStateProperty)
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateOverlay, Avalonia.Threading.DispatcherPriority.Background);
        };
        _overlay.PositionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateOverlay, Avalonia.Threading.DispatcherPriority.Background);
        UpdateOverlay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_owner != null)
        {
            _owner.LayoutUpdated -= OnOwnerMoved;
            _owner.PositionChanged -= OnOwnerMoved;
        }
        if (_overlay != null)
        {
            _overlay.Content = null;
            _overlay.Close();
            _overlay = null;
        }
        _owner = null;
    }

    private void OnOwnerMoved(object? sender, EventArgs e) => UpdateOverlay();

    private void UpdateOverlay()
    {
        var overlay = _overlay;
        if (overlay == null || _owner == null) return;
        bool show = IsEffectivelyVisible && Bounds.Width > 1 && Bounds.Height > 1 && VisualRoot != null;
        if (!show)
        {
            if (overlay.IsVisible) overlay.Hide();
            return;
        }

        // Full-screen overlays are managed by the window (see MainWindow): leave size/position alone.
        if (overlay.WindowState != WindowState.FullScreen)
        {
            var pos = this.PointToScreen(new Point(0, 0));
            if (overlay.Position != pos) overlay.Position = pos;
            // Compare with the window's REAL size: if the window manager changed it behind our back,
            // the Width/Height properties still hold the old request and setting them again would be
            // a no-op, so nudge by a hair to force a real resize.
            if (Math.Abs(overlay.ClientSize.Width - Bounds.Width) > 1)
                overlay.Width = Math.Abs(overlay.Width - Bounds.Width) < 0.01 ? Bounds.Width + 0.01 : Bounds.Width;
            if (Math.Abs(overlay.ClientSize.Height - Bounds.Height) > 1)
                overlay.Height = Math.Abs(overlay.Height - Bounds.Height) < 0.01 ? Bounds.Height + 0.01 : Bounds.Height;
        }
        if (!overlay.IsVisible) overlay.Show(_owner);
    }
}
