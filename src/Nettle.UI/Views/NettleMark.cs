using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nettle.UI.Views;

/// <summary>
/// Nettle's logo shape: a sea nettle jellyfish silhouette (Assets/nettle-glyph.png, white on
/// transparent), painted with any brush, so the logo can take each theme's colours.
/// </summary>
public sealed class NettleMark : Avalonia.Controls.Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<NettleMark, IBrush?>(nameof(Fill), Brushes.White);

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    static NettleMark() => AffectsRender<NettleMark>(FillProperty);

    private static Bitmap? _glyph;
    internal static Bitmap Glyph => _glyph ??= new Bitmap(AssetLoader.Open(new Uri("avares://Nettle.UI/Assets/nettle-glyph.png")));

    public override void Render(DrawingContext context)
    {
        var fill = Fill;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (fill == null || size <= 0) return;
        var rect = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        var mask = new ImageBrush(Glyph)
        {
            Stretch = Stretch.Uniform,
            DestinationRect = new RelativeRect(rect, RelativeUnit.Absolute),
        };
        using (context.PushOpacityMask(mask, rect))
            context.FillRectangle(fill, rect);
    }
}
