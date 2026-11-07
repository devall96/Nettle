using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Slopfin.UI.ViewModels;

namespace Slopfin.UI.Views;

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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (RowsHost.Bounds.Width > 0) UpdateColumns(RowsHost.Bounds.Width);
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
