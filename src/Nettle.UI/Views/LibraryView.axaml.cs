using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Nettle.UI.ViewModels;

namespace Nettle.UI.Views;

public partial class LibraryView : UserControl
{
    private ScrollViewer? _scroll;

    public LibraryView()
    {
        InitializeComponent();
        RowsHost.TemplateApplied += (_, e) =>
        {
            _scroll = e.NameScope.Find<ScrollViewer>("PART_Scroll");
            if (_scroll != null) _scroll.ScrollChanged += OnScrollChanged;
        };
        RowsHost.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
    }

    private LibraryViewModel? Vm => DataContext as LibraryViewModel;

    private LibraryViewModel? _hooked;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hooked != null) _hooked.ScrollToRow -= OnScrollToRow;
        _hooked = Vm;
        if (_hooked != null) _hooked.ScrollToRow += OnScrollToRow;
        if (RowsHost.Bounds.Width > 0) UpdateColumns(RowsHost.Bounds.Width);
    }

    /// <summary>A–Z jump: bring the row into view, then line it up with the top.</summary>
    private void OnScrollToRow(int row)
    {
        if (_scroll == null || row < 0 || row >= RowsHost.ItemCount) return;
        RowsHost.ScrollIntoView(row);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_scroll == null || RowsHost.ContainerFromIndex(row) is not Control c) return;
            var p = c.TranslatePoint(new Point(0, 0), _scroll.Content as Visual ?? _scroll);
            if (p is { } pt) _scroll.Offset = new Vector(_scroll.Offset.X, Math.Max(0, pt.Y - 8));
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateColumns(double width)
    {
        var vm = Vm;
        if (vm == null || width <= 0) return;
        double usable = width - 56 - 40 - 12; // padding + scrollbar
        double cell = vm.CardWidth + 18;      // card + right margin
        vm.SetColumns((int)Math.Max(1, Math.Floor((usable + 18) / cell)));
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll == null || Vm == null) return;
        if (_scroll.Offset.Y + _scroll.Viewport.Height > _scroll.Extent.Height - 900)
            _ = Vm.LoadMoreAsync();
    }
}
