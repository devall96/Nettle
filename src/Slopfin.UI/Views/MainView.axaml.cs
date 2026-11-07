using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Slopfin.UI.Controls;
using Slopfin.UI.ViewModels;

namespace Slopfin.UI.Views;

public partial class MainView : UserControl
{
    private TopLevel? _top;

    public MainView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top == null) return;
        _top.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        _top.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_top == null) return;
        _top.RemoveHandler(KeyDownEvent, OnKeyDown);
        _top.RemoveHandler(PointerPressedEvent, OnPointerPressed);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || e.Handled) return;

        if (vm.Player != null)
        {
            if (vm.Player.HandleKey(e.Key)) e.Handled = true;
            return;
        }

        bool inText = e.Source is TextBox;
        switch (e.Key)
        {
            case Key.Escape:
            case Key.BrowserBack:
            case Key.Back when !inText:
                if (AppServices.Window?.IsFullScreen == true && vm.Shell == null) AppServices.Window.SetFullScreen(false);
                vm.Shell?.GoBack();
                e.Handled = true;
                break;
            case Key.F11:
                if (AppServices.Window is { } w) w.SetFullScreen(!w.IsFullScreen);
                e.Handled = true;
                break;
            case Key.Left when !inText:
            case Key.Right when !inText:
            case Key.Up:
            case Key.Down:
                if (_top == null) break;
                var dir = e.Key switch
                {
                    Key.Left => SpatialNavigation.Direction.Left,
                    Key.Right => SpatialNavigation.Direction.Right,
                    Key.Up => SpatialNavigation.Direction.Up,
                    _ => SpatialNavigation.Direction.Down
                };
                if (SpatialNavigation.TryMove(_top, dir)) e.Handled = true;
                break;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = Vm;
        if (vm?.Player != null) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed)
        {
            vm?.Shell?.GoBack();
            e.Handled = true;
        }
    }
}
