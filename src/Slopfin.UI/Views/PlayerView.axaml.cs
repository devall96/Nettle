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

        // The overlay lives in its own window, so wire its input directly.
        Overlay.PointerMoved += (_, _) => Vm?.ShowOsd();
        Overlay.PointerPressed += OnOverlayPressed;
        Overlay.AttachedToVisualTree += (_, _) =>
        {
            _overlayTop = TopLevel.GetTopLevel(Overlay);
            if (_overlayTop != null && _overlayTop != TopLevel.GetTopLevel(this))
                _overlayTop.AddHandler(KeyDownEvent, OnOverlayKey, RoutingStrategies.Bubble);
        };

        SeekBar.AddHandler(PointerPressedEvent, (_, _) => { if (Vm != null) Vm.IsSeeking = true; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => Vm?.CommitSeek(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => { if (Vm?.IsSeeking == true) Vm.CommitSeek(); }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private PlayerViewModel? Vm => DataContext as PlayerViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // The floating overlay doesn't inherit our DataContext automatically.
        Overlay.DataContext = DataContext;
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
        if (vm == null || e.Source != Overlay && e.Source is not Panel) return;
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
