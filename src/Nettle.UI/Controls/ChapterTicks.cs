using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nettle.UI.Controls;

/// <summary>
/// Small marks where chapters start, drawn over a seek bar. <see cref="Marks"/> are fractions
/// (0–1) of the length; <see cref="Inset"/> matches the slider's track inset at each end.
/// </summary>
public sealed class ChapterTicks : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> MarksProperty =
        AvaloniaProperty.Register<ChapterTicks, IReadOnlyList<double>?>(nameof(Marks));

    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<ChapterTicks, double>(nameof(Inset), 10);

    public IReadOnlyList<double>? Marks { get => GetValue(MarksProperty); set => SetValue(MarksProperty, value); }
    public double Inset { get => GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

    static ChapterTicks() => AffectsRender<ChapterTicks>(MarksProperty, InsetProperty);

    public override void Render(DrawingContext context)
    {
        var marks = Marks;
        if (marks == null || marks.Count == 0) return;
        double w = Bounds.Width - 2 * Inset, mid = Bounds.Height / 2;
        if (w <= 0) return;
        foreach (var f in marks)
        {
            double x = Inset + f * w;
            context.FillRectangle(Fill, new Rect(x - 1, mid - 5, 2, 10), 1);
        }
    }
}
