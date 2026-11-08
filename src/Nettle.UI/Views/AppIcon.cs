using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nettle.UI.Theming;

namespace Nettle.UI.Views;

/// <summary>
/// The app icon: the white sea nettle on a rounded tile with the theme's accent gradient. The
/// icon files in Nettle.Desktop/Assets use the default theme and are rendered from this with
/// <c>Nettle --render-icon &lt;folder&gt;</c>; the window / taskbar icon follows the chosen theme.
/// </summary>
public static class AppIcon
{
    public static Control CreateTile(double size, ThemePalette? palette = null)
    {
        var p = palette ?? ThemeManager.Default;
        return new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.25),
            ClipToBounds = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(p.Accent, 0), new GradientStop(p.Accent2, 1) }
            },
            Child = new NettleMark { Fill = new SolidColorBrush(p.OnAccent), Margin = new Thickness(size * 0.14) },
        };
    }

    public static Bitmap Render(int size, ThemePalette? palette = null)
    {
        var tile = CreateTile(size, palette);
        tile.Measure(new Size(size, size));
        tile.Arrange(new Rect(0, 0, size, size));
        tile.UpdateLayout();
        var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bmp.Render(tile);
        return bmp;
    }

    private static byte[] RenderPng(int size, ThemePalette? palette = null)
    {
        using var bmp = Render(size, palette);
        using var ms = new MemoryStream();
        bmp.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A Windows .ico (PNG-compressed entries) of the tile in the given theme.</summary>
    public static byte[] RenderIco(ThemePalette? palette = null, params int[] sizes)
    {
        if (sizes.Length == 0) sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s => RenderPng(s, palette)).ToArray();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        uint offset = 6u + 16u * (uint)sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)images[i].Length); w.Write(offset);
            offset += (uint)images[i].Length;
        }
        foreach (var img in images) w.Write(img);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Android TV's static icons in the default theme, written into the app's <c>res</c> folder:
    /// the launcher icon at every density and the TV home-screen banner (320×180 dp: the icon
    /// tile and the "Nettle" wordmark on the theme's background). Rendered with
    /// <c>Nettle --render-android android/app/src/main/res</c>; inside the app the logo follows the chosen theme.
    /// </summary>
    public static void RenderAndroidFiles(string resFolder)
    {
        var p = ThemeManager.Default;
        foreach (var (dir, px) in new[] { ("mdpi", 48), ("hdpi", 72), ("xhdpi", 96), ("xxhdpi", 144), ("xxxhdpi", 192) })
        {
            Directory.CreateDirectory(Path.Combine(resFolder, "mipmap-" + dir));
            File.WriteAllBytes(Path.Combine(resFolder, "mipmap-" + dir, "ic_launcher.png"), RenderPng(px, p));
        }
        foreach (var (dir, scale) in new[] { ("xhdpi", 1.0), ("xxhdpi", 1.5), ("xxxhdpi", 2.0) })
        {
            Directory.CreateDirectory(Path.Combine(resFolder, "drawable-" + dir));
            SaveTvBanner(Path.Combine(resFolder, "drawable-" + dir, "banner.png"), scale, p);
        }
    }

    /// <summary>The TV launcher banner at <paramref name="scale"/> × 320×180.</summary>
    private static void SaveTvBanner(string path, double scale, ThemePalette p)
    {
        int w = (int)Math.Round(320 * scale), h = (int)Math.Round(180 * scale);
        double tileSize = 118 * scale;
        var root = new Grid
        {
            Width = w, Height = h,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(p.Background, 0), new GradientStop(p.SurfaceHigh, 1) }
            },
        };
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 12 * scale,
            Margin = new Thickness(16 * scale, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        row.Children.Add(CreateTile(tileSize, p));
        row.Children.Add(new TextBlock
        {
            Text = "Nettle", FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 54 * scale, FontWeight = FontWeight.ExtraBold,
            LetterSpacing = -1.5 * scale, Foreground = new SolidColorBrush(p.Text), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        root.Children.Add(row);
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        using var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        bmp.Render(root);
        bmp.Save(path);
    }

    /// <summary>Writes nettle.png (256 px) and nettle.ico (16–256 px) in the default theme into <paramref name="folder"/>.</summary>
    public static void RenderFiles(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "nettle.png"), RenderPng(256));
        File.WriteAllBytes(Path.Combine(folder, "nettle.ico"), RenderIco());
        // Previews for checking (not shipped): every theme side by side, and the 32 px size.
        File.WriteAllBytes(Path.Combine(folder, "preview-32.png"), RenderPng(32));
        var all = ThemeManager.All;
        var strip = new WrapPanel { Width = 8 * 136, Background = Brushes.Black };
        foreach (var t in all) strip.Children.Add(new Border { Margin = new Thickness(4), Child = CreateTile(128, t) });
        int rows = (all.Count + 7) / 8;
        strip.Measure(new Size(8 * 136, rows * 136));
        strip.Arrange(new Rect(0, 0, 8 * 136, rows * 136));
        strip.UpdateLayout();
        using var bmp = new RenderTargetBitmap(new PixelSize(8 * 136, rows * 136), new Vector(96, 96));
        bmp.Render(strip);
        bmp.Save(Path.Combine(folder, "preview-themes.png"));
    }
}
