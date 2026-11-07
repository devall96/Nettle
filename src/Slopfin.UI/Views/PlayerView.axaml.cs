using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Slopfin.UI.ViewModels;

namespace Slopfin.UI.Views;

public partial class PlayerView : UserControl
{
    private TopLevel? _overlayTop;

    public PlayerView()
    {
        InitializeComponent();

        // The overlay may live in its own window (Windows), so wire its input directly.
        Overlay.PointerMoved += (_, _) => Vm?.UserActivity();
        Overlay.PointerPressed += OnOverlayPressed;

        // Scrubbing preview: follows the pointer over the seek bar, and the thumb while dragging.
        SeekBar.PointerMoved += (_, e) => UpdateTrickplay(e.GetPosition(SeekBar).X);
        SeekBar.PointerExited += (_, _) => { if (Vm is { IsSeeking: false } vm) vm.HideTrickplay(); };
        SeekBar.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && Vm is { IsSeeking: true } vm && SeekBar.Maximum > 0)
                UpdateTrickplay(SeekBar.Value / SeekBar.Maximum * SeekBar.Bounds.Width, SeekBar.Value);
        };
        Overlay.AttachedToVisualTree += (_, _) =>
        {
            _overlayTop = TopLevel.GetTopLevel(Overlay);
            if (_overlayTop != null && _overlayTop != TopLevel.GetTopLevel(this))
                _overlayTop.AddHandler(KeyDownEvent, OnOverlayKey, RoutingStrategies.Bubble);
        };

        SeekBar.AddHandler(PointerPressedEvent, (_, _) => { if (Vm != null) Vm.IsSeeking = true; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => { Vm?.CommitSeek(); if (!SeekBar.IsPointerOver) Vm?.HideTrickplay(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => { if (Vm?.IsSeeking == true) Vm.CommitSeek(); }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private PlayerViewModel? Vm => DataContext as PlayerViewModel;

    /// <summary>Places the preview above the seek bar at <paramref name="x"/> (seek bar coordinates).</summary>
    private void UpdateTrickplay(double x, double? ms = null)
    {
        var vm = Vm;
        if (vm == null || SeekBar.Bounds.Width < 10 || SeekBar.Maximum <= 0) return;
        // The slider's track is inset by half its thumb on each side.
        const double inset = 10;
        var frac = Math.Clamp((x - inset) / Math.Max(1, SeekBar.Bounds.Width - 2 * inset), 0, 1);
        vm.ShowTrickplay(ms ?? frac * SeekBar.Maximum);
        var w = TrickPreview.Bounds.Width > 0 ? TrickPreview.Bounds.Width : vm.TrickWidth + 10;
        var h = TrickPreview.Bounds.Height > 0 ? TrickPreview.Bounds.Height : vm.TrickHeight + 34;
        Canvas.SetLeft(TrickPreview, Math.Clamp(x - w / 2, -40, SeekBar.Bounds.Width - w + 40));
        Canvas.SetTop(TrickPreview, -h - 14);
    }

    private PlayerViewModel? _vm;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // The floating overlay doesn't inherit our DataContext automatically.
        Overlay.DataContext = DataContext;
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = Vm;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Remote / keyboard users land on "Continue watching" when the question appears.
        if (e.PropertyName == nameof(PlayerViewModel.IsStillWatchingVisible) && _vm?.IsStillWatchingVisible == true)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ContinueButton.Focus(), Avalonia.Threading.DispatcherPriority.Input);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PlayPause.Focus();
    }

    private DateTime _lastClick;

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        vm.UserActivity();
        if (vm.IsStillWatchingVisible || e.Source != Overlay && e.Source is not Panel) return;
        if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastClick).TotalMilliseconds < 350)
        {
            vm.ToggleFullScreenCommand.Execute(null);
            _lastClick = DateTime.MinValue;
        }
        else
        {
            _lastClick = now;
            if (vm.IsOsdVisible && !vm.IsTracksOpen) vm.TogglePlayCommand.Execute(null);
            else vm.ShowOsd();
            vm.IsTracksOpen = false;
        }
    }

    private void OnOverlayKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Vm == null) return;
        if (Vm.HandleKey(e.Key)) e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _overlayTop?.RemoveHandler(KeyDownEvent, OnOverlayKey);
    }
}
