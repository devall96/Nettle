using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nettle.UI.ViewModels;

namespace Nettle.UI.Views;

public partial class AccountPickerView : UserControl
{
    public AccountPickerView() => InitializeComponent();
}

public partial class ServerSelectView : UserControl
{
    public ServerSelectView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => AddressBox.Focus(), DispatcherPriority.Input);
    }
}

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is LoginViewModel vm)
            vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.PasswordFocusRequested))
            Dispatcher.UIThread.Post(() => PasswordBox.Focus(), DispatcherPriority.Input);
        else if (e.PropertyName == nameof(LoginViewModel.ShowManualForm) && sender is LoginViewModel { ShowManualForm: true } vm)
            Dispatcher.UIThread.Post(() => (string.IsNullOrEmpty(vm.Username) ? UserBox : PasswordBox).Focus(), DispatcherPriority.Input);
    }
}

public partial class ShellView : UserControl
{
    private bool _pointerInRail;
    private bool _keyboardInRail;

    public ShellView()
    {
        InitializeComponent();

        Rail.PointerEntered += (_, _) => { _pointerInRail = true; UpdateRail(); };
        Rail.PointerExited += (_, _) => { _pointerInRail = false; UpdateRail(); };

        // Keyboard / remote focus entering the rail expands it (so labels are readable on TV).
        Rail.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.NavigationMethod != NavigationMethod.Pointer) { _keyboardInRail = true; UpdateRail(); }
        }, RoutingStrategies.Bubble);
        Rail.AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused == null || !Rail.IsVisualAncestorOf(focused)) { _keyboardInRail = false; UpdateRail(); }
        }), RoutingStrategies.Bubble);

        // Choosing a destination hands focus to the page and collapses the rail.
        Rail.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button { DataContext: NavItemViewModel })
            {
                _keyboardInRail = false;
                _pointerInRail = false;
                UpdateRail();
                Dispatcher.UIThread.Post(() => PageHost.Focus(NavigationMethod.Unspecified), DispatcherPriority.Input);
            }
        }, RoutingStrategies.Bubble);
    }

    private void UpdateRail() => Rail.Classes.Set("expanded", _pointerInRail || _keyboardInRail);
}

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();
}

public partial class DetailsView : UserControl
{
    public DetailsView() => InitializeComponent();
}

public partial class SearchView : UserControl
{
    public SearchView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
    }
}

public partial class RequestsView : UserControl
{
    public RequestsView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public partial class LiveTvView : UserControl
{
    private LiveTvViewModel? _vm;
    private double? _pendingScrollX;
    private bool _syncing;

    public LiveTvView()
    {
        InitializeComponent();
        // The schedule scrolls freely; the time header follows it sideways and the channel
        // column follows it up and down.
        GridScroll.ScrollChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            HeaderScroll.Offset = new Avalonia.Vector(GridScroll.Offset.X, 0);
            ChannelScroll.Offset = new Avalonia.Vector(0, GridScroll.Offset.Y);
            _syncing = false;
            _vm?.OnScrolled(GridScroll.Offset.X);
        };
        // Scrolling over the channel column scrolls the guide too.
        ChannelScroll.ScrollChanged += (_, _) =>
        {
            if (_syncing) return;
            _syncing = true;
            GridScroll.Offset = new Avalonia.Vector(GridScroll.Offset.X, ChannelScroll.Offset.Y);
            _syncing = false;
        };
        // A requested jump (e.g. "now") waits until the grid has been laid out wide enough.
        GridScroll.LayoutUpdated += (_, _) => ApplyPendingScroll();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.ScrollRequested -= OnScrollRequested;
        _vm = DataContext as LiveTvViewModel;
        if (_vm != null) _vm.ScrollRequested += OnScrollRequested;
    }

    private void OnScrollRequested(double x)
    {
        _pendingScrollX = x;
        ApplyPendingScroll();
    }

    private void ApplyPendingScroll()
    {
        if (_pendingScrollX is not { } x || GridScroll.Extent.Width <= GridScroll.Viewport.Width && x > 0) return;
        _pendingScrollX = null;
        var max = Math.Max(0, GridScroll.Extent.Width - GridScroll.Viewport.Width);
        GridScroll.Offset = new Avalonia.Vector(Math.Clamp(x, 0, max), GridScroll.Offset.Y);
    }

    private void OnProgramPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: GuideProgramViewModel p }) p.FocusCommand.Execute(null);
    }

    private void OnProgramGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is Control { DataContext: GuideProgramViewModel p }) p.FocusCommand.Execute(null);
    }
}
