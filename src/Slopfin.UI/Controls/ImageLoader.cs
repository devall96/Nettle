using Avalonia;
using Avalonia.Controls;

namespace Slopfin.UI.Controls;

/// <summary>
/// Attached properties for async, cached, size-aware image loading with a fade-in.
/// Usage: &lt;Image c:ImageLoader.DecodeWidth="300" c:ImageLoader.Source="{Binding Url}" Classes="fade"/&gt;
/// </summary>
public sealed class ImageLoader
{
    private ImageLoader() { }

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, string?>("Source");

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, int>("DecodeWidth", 0);

    public static string? GetSource(Image o) => o.GetValue(SourceProperty);
    public static void SetSource(Image o, string? v) => o.SetValue(SourceProperty, v);
    public static int GetDecodeWidth(Image o) => o.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(Image o, int v) => o.SetValue(DecodeWidthProperty, v);

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>((img, e) => _ = LoadAsync(img, e.NewValue as string));
    }

    private static async Task LoadAsync(Image img, string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            img.Source = null;
            return;
        }

        int width = GetDecodeWidth(img);
        if (width <= 0) width = img.Width > 0 && !double.IsNaN(img.Width) ? (int)(img.Width * 1.5) : 0;

        var cache = AppServices.Images;
        var hit = cache.TryGetMemory(url, width);
        if (hit != null)
        {
            img.Source = hit;
            img.Opacity = 1;
            return;
        }

        img.Opacity = 0;
        img.Source = null;
        var bmp = await cache.LoadAsync(url, width);
        if (GetSource(img) != url) return; // the control was recycled for another item meanwhile
        img.Source = bmp;
        img.Opacity = bmp == null ? 0 : 1;
    }
}
