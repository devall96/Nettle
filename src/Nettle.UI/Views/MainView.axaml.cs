using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Nettle.UI.Controls;
using Nettle.UI.ViewModels;

namespace Nettle.UI.Views;

public partial class MainView : UserControl
{
    private TopLevel? _top;

    private readonly Avalonia.Animation.IPageTransition? _crossFade;

    public MainView()
    {
        InitializeComponent();
        _crossFade = RootHost.PageTransition;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm) return;
        ApplyMotion(vm.MotionEnabled);
        vm.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(MainViewModel.MotionEnabled)) ApplyMotion(vm.MotionEnabled); };
    }

    /// <summary>Reduce motion also drops the cross-fade between screens.</summary>
    private void ApplyMotion(bool on) => RootHost.PageTransition = on ? _crossFade : null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        // Video below the UI (Android TV): the top level must not paint a background of its own.
        if (AppServices.Profile.VideoBehindUi && _top != null) _top.Background = Avalonia.Media.Brushes.Transparent;
        if (_top == null) return;
        _top.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        if (AppServices.Profile.IsTv) _top.AddHandler(KeyDownEvent, OnTvPlayerKey, RoutingStrategies.Tunnel);
        if (AppServices.Profile.IsTv)
        {
            // Pages fill in asynchronously; keep a visible focus on something the remote can use.
            _focusTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(400), Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
            {
                if (_top != null && Vm is { IsStarting: false, Player: null }) SpatialNavigation.EnsureFocus(_top);
            });
            _focusTimer.Start();
        }
#if DEBUG || NETTLE_DEV
        // Developer aid: NETTLE_DEV_FOCUSLOG=1 logs which control has focus after every key.
        if (Environment.GetEnvironmentVariable("NETTLE_DEV_FOCUSLOG") == "1")
            _top.AddHandler(KeyUpEvent, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(LogFocus, Avalonia.Threading.DispatcherPriority.Background), RoutingStrategies.Bubble, handledEventsToo: true);
#endif
        _top.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_top == null) return;
        _top.RemoveHandler(KeyDownEvent, OnKeyDown);
        _top.RemoveHandler(KeyDownEvent, OnTvPlayerKey);
        _focusTimer?.Stop();
        _top.RemoveHandler(PointerPressedEvent, OnPointerPressed);
    }

    private Avalonia.Threading.DispatcherTimer? _focusTimer;
    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// TV remote in the player, seen BEFORE the focused control: the player decides whether a key
    /// is a shortcut, moves focus between its controls, or goes to the focused button (OK).
    /// </summary>
    private void OnTvPlayerKey(object? sender, KeyEventArgs e)
    {
        if (_top == null || e.Handled) return;
        if (OpenKeyboardOnOk(e)) return;
        if (Vm?.Player is not { } player)
        {
            OnTvPageKey(e);
            return;
        }
        if (e.Source is TextBox && e.Key is not (Key.Escape or Key.Back)) return;
        var focused = _top.FocusManager?.GetFocusedElement() as Control;
        bool inPlayer = focused != null && focused.FindAncestorOfType<PlayerView>() != null;
        bool onButton = inPlayer && focused is Button or ToggleButton or ListBoxItem;
        switch (player.HandleTvKey(e.Key, onButton))
        {
            case PlayerViewModel.TvKeyResult.Handled:
                e.Handled = true;
                break;
            case PlayerViewModel.TvKeyResult.MoveFocus when e.Key is Key.Left or Key.Right or Key.Up or Key.Down:
                e.Handled = true;
                if (!inPlayer) { FocusPlayPause(); break; }
                SpatialNavigation.TryMove(_top, e.Key switch
                {
                    Key.Left => SpatialNavigation.Direction.Left,
                    Key.Right => SpatialNavigation.Direction.Right,
                    Key.Up => SpatialNavigation.Direction.Up,
                    _ => SpatialNavigation.Direction.Down,
                });
                break;
        }
    }

    private void LogFocus()
    {
        var f = _top?.FocusManager?.GetFocusedElement() as Control;
        var text = f switch
        {
            Button { Content: string s } => s,
            ContentControl { DataContext: { } dc } when dc != DataContext => dc.ToString(),
            _ => null,
        };
        var b = f != null && _top != null ? f.TransformToVisual(_top) is { } t ? new Rect(f.Bounds.Size).TransformToAABB(t) : (Rect?)null : null;
        Nettle.Core.Services.Log.Info($"focus: {f?.GetType().Name ?? "(none)"} name={f?.Name} text={text} classes={(f == null ? "" : string.Join(".", f.Classes))} at {b}");
    }

    /// <summary>
    /// TV remote on the pages: the D-pad always moves focus the TV way (see SpatialNavigation)
    /// before scroll viewers or Avalonia's own arrow handling can take the key. Left / Right still
    /// edit text in a text box, and an open drop-down keeps its own keys.
    /// </summary>
    private void OnTvPageKey(KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down) || _top == null) return;
        var focused = _top.FocusManager?.GetFocusedElement() as Control;
        if (focused is TextBox && e.Key is Key.Left or Key.Right) return;
        if (focused?.FindAncestorOfType<ComboBox>(includeSelf: true) is { IsDropDownOpen: true }) return;
        if (focused is ComboBoxItem || focused?.FindAncestorOfType<Popup>() != null) return;
        var dir = e.Key switch
        {
            Key.Left => SpatialNavigation.Direction.Left,
            Key.Right => SpatialNavigation.Direction.Right,
            Key.Up => SpatialNavigation.Direction.Up,
            _ => SpatialNavigation.Direction.Down,
        };
        // Nothing further in that direction: the key is still used up, so focus never jumps
        // somewhere unexpected.
        SpatialNavigation.TryMove(_top, dir);
        e.Handled = true;
    }

    /// <summary>TV: OK on a text box opens the on-screen keyboard (focus alone doesn't, see Tv.axaml).</summary>
    private bool OpenKeyboardOnOk(KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || _top?.FocusManager?.GetFocusedElement() is not TextBox tb) return false;
        if (InputMethod.GetIsInputMethodEnabled(tb)) return false;
        InputMethod.SetIsInputMethodEnabled(tb, true);
        tb.LostFocus -= OnKeyboardBoxLostFocus;
        tb.LostFocus += OnKeyboardBoxLostFocus;
        // The keyboard follows focus: hand focus away and back so the platform asks for it now.
        _top.FocusManager.ClearFocus();
        tb.Focus(NavigationMethod.Directional);
        e.Handled = true;
        return true;
    }

    private static void OnKeyboardBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        tb.LostFocus -= OnKeyboardBoxLostFocus;
        tb.ClearValue(InputMethod.IsInputMethodEnabledProperty);
    }

    private void FocusPlayPause()
    {
        var play = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "PlayPause" && b.IsEffectivelyVisible);
        play?.Focus(NavigationMethod.Directional);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null || e.Handled) return;

        if (vm.Player != null)
        {
            // Typing in a box (e.g. the subtitle language) isn't a player shortcut.
            if (e.Source is TextBox && e.Key is not (Key.Escape or Key.Enter)) return;
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
