using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Slopfin.UI.Controls;

/// <summary>
/// Attached properties for async, cached, size-aware image loading with a fade-in.
/// Usage: &lt;Image c:ImageLoader.DecodeWidth="300" c:ImageLoader.Source="{Binding Url}" Classes="fade"/&gt;
///
/// Images are always decoded at (roughly) their display size, never the source's full size, and
/// a load is cancelled as soon as its Image is recycled for another item or leaves the screen,
/// so fast scrolling doesn't queue up downloads nobody will see.
/// </summary>
public sealed class ImageLoader
{
    private ImageLoader() { }

    /// <summary>Used when neither DecodeWidth nor a layout width is known: about one screen wide.</summary>
    private const int FallbackWidth = 1280;

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, string?>("Source");

    public static readonly AttachedProperty<int> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<ImageLoader, Image, int>("DecodeWidth", 0);

    public static string? GetSource(Image o) => o.GetValue(SourceProperty);
    public static void SetSource(Image o, string? v) => o.SetValue(SourceProperty, v);
    public static int GetDecodeWidth(Image o) => o.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(Image o, int v) => o.SetValue(DecodeWidthProperty, v);

    private sealed class State
    {
        public CancellationTokenSource? Cts;
        public string? LoadedUrl;
        public int LoadedWidth;
        public bool Hooked;
        public int Generation;
    }

    private static readonly ConditionalWeakTable<Image, State> States = new();

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>((img, _) => Schedule(img));
        // DecodeWidth may be bound after Source (binding order isn't guaranteed): reload at the right size.
        DecodeWidthProperty.Changed.AddClassHandler<Image>((img, _) => Schedule(img));
    }

    private static void Schedule(Image img)
    {
        var st = States.GetOrCreateValue(img);
        if (!st.Hooked)
        {
            st.Hooked = true;
            // Leaving the screen (row virtualized away, page closed) abandons the pending load.
            img.DetachedFromVisualTree += (_, _) =>
            {
                if (!States.TryGetValue(img, out var s) || s.Cts == null) return;
                if (s.LoadedUrl == null) { s.Cts.Cancel(); s.Cts = null; }
            };
            img.AttachedToVisualTree += (_, _) =>
            {
                if (States.TryGetValue(img, out var s) && s.LoadedUrl == null && s.Cts == null && GetSource(img) != null) Schedule(img);
            };
        }

        var url = GetSource(img);
        int width = EffectiveWidth(img);
        if (url == st.LoadedUrl && width == st.LoadedWidth && img.Source != null) return;

        st.Cts?.Cancel();
        st.Cts = null;
        st.LoadedUrl = null;
        var generation = ++st.Generation;
        if (string.IsNullOrEmpty(url))
        {
            img.Source = null;
            return;
        }

        if (width <= 0)
        {
            // No size known yet: wait one layout pass and use the real width.
            Dispatcher.UIThread.Post(() =>
            {
                if (st.Generation != generation || GetSource(img) != url) return;
                _ = LoadAsync(img, url, LayoutWidth(img) ?? FallbackWidth);
            }, DispatcherPriority.Loaded);
            return;
        }
        _ = LoadAsync(img, url, width);
    }

    private static int EffectiveWidth(Image img)
    {
        int w = GetDecodeWidth(img);
        if (w > 0) return w;
        if (img.Width > 0 && !double.IsNaN(img.Width)) return (int)(img.Width * 1.5);
        return 0;
    }

    private static int? LayoutWidth(Image img)
    {
        var w = img.Bounds.Width;
        if (w < 1) return null;
        var scale = TopLevel.GetTopLevel(img)?.RenderScaling ?? 1;
        return (int)Math.Ceiling(w * scale);
    }

    private static async Task LoadAsync(Image img, string url, int width)
    {
        var st = States.GetOrCreateValue(img);
        var cache = AppServices.Images;
        var hit = cache.TryGetMemory(url, width);
        if (hit != null)
        {
            img.Source = hit;
            img.Opacity = 1;
            st.LoadedUrl = url;
            st.LoadedWidth = width;
            return;
        }

        st.Cts?.Cancel();
        var cts = st.Cts = new CancellationTokenSource();
        img.Opacity = 0;
        img.Source = null;
        var bmp = await cache.LoadAsync(url, width, cts.Token);
        if (cts.IsCancellationRequested || GetSource(img) != url) return; // recycled for another item meanwhile
        img.Source = bmp;
        img.Opacity = bmp == null ? 0 : 1;
        st.LoadedUrl = url;
        st.LoadedWidth = width;
        if (st.Cts == cts) st.Cts = null;
    }
}
