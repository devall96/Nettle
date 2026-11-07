using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Slopfin.UI.Theming;

namespace Slopfin.UI.Views;

/// <summary>
/// Artwork for the installer's splash screen, drawn by Slopfin itself so the installer always
/// matches the app's look (theme colours, Inter typeface, logo). Rendered at build time by
/// <c>Slopfin.exe --render-splash &lt;file.png&gt;</c> (see build-release.ps1).
/// </summary>
public static class InstallerSplash
{
    public const int Width = 640, Height = 360;

    public static void RenderTo(string path, string version)
    {
        var p = ThemeManager.Find("midnight");
        var inter = new FontFamily("fonts:Inter#Inter");

        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(p.Accent, 0), new GradientStop(p.Accent2, 1) }
        };

        var tile = new Border
        {
            Width = 88, Height = 88, CornerRadius = new CornerRadius(26), Background = gradient,
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M5,27 C11,23 15,13 27,6 C24,14 24,21 30,27 Z M4,30 L32,30"),
                Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 2.4,
                StrokeLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Margin = new Thickness(18),
            }
        };

        var word = new TextBlock
        {
            Text = "Slopfin", FontFamily = inter, FontSize = 54, FontWeight = FontWeight.ExtraBold,
            LetterSpacing = -1.5, Foreground = new SolidColorBrush(p.Text), VerticalAlignment = VerticalAlignment.Center,
        };

        var tagline = new TextBlock
        {
            Text = "Your Jellyfin, in full quality.", FontFamily = inter, FontSize = 17, FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(p.TextMuted), HorizontalAlignment = HorizontalAlignment.Center,
        };

        var status = new TextBlock
        {
            Text = $"Installing version {version}…", FontFamily = inter, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(p.Accent), HorizontalAlignment = HorizontalAlignment.Center,
        };

        var root = new Grid
        {
            Width = Width, Height = Height,
            Background = new SolidColorBrush(p.Background),
        };
        // Ambient glow, as in the app.
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.15, 0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.15, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(1.0, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0x40, p.Accent.R, p.Accent.G, p.Accent.B), 0), new GradientStop(Color.FromArgb(0, p.Accent.R, p.Accent.G, p.Accent.B), 1) }
            }
        });
        root.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.95, 1, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.95, 1, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0x30, p.Accent2.R, p.Accent2.G, p.Accent2.B), 0), new GradientStop(Color.FromArgb(0, p.Accent2.R, p.Accent2.G, p.Accent2.B), 1) }
            }
        });
        root.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Spacing = 14,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center, Children = { tile, word } },
                tagline,
                status,
            }
        });
        // Subtle frame so the borderless splash window reads as a card.
        root.Children.Add(new Border { BorderBrush = new SolidColorBrush(p.Border), BorderThickness = new Thickness(1) });

        Save(root, Width, Height, 1, path);
    }

    private static void Save(Control root, int w, int h, double scale, string path)
    {
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(w * scale), (int)(h * scale)), new Vector(96 * scale, 96 * scale));
        bmp.Render(root);
        bmp.Save(path);
    }

    private static Border Tile(ThemePalette p, double size)
    {
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(p.Accent, 0), new GradientStop(p.Accent2, 1) }
            },
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M5,27 C11,23 15,13 27,6 C24,14 24,21 30,27 Z M4,30 L32,30"),
                Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 2.4,
                StrokeLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.2),
            }
        };
    }

    private static RadialGradientBrush Glow(Color c, byte alpha, double x, double y, double r) => new()
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(r, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(r * 1.8, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) }
    };

    /// <summary>
    /// README banner (1280×400, rendered at 2× for sharp display). Same theme, icon and Inter
    /// typeface as the app. Rendered with <c>Slopfin.exe --render-banner &lt;file.png&gt;</c>.
    /// </summary>
    public static void RenderBanner(string path)
    {
        const int w = 1280, h = 400;
        var p = ThemeManager.Find("midnight");
        var inter = new FontFamily("fonts:Inter#Inter");

        var root = new Grid { Width = w, Height = h, Background = new SolidColorBrush(p.Background), ClipToBounds = true };
        root.Children.Add(new Border { Background = Glow(p.Accent, 0x55, 0.12, 0.0, 0.55) });
        root.Children.Add(new Border { Background = Glow(p.Accent2, 0x40, 0.92, 1.0, 0.5) });

        // Large faint fin watermark on the right.
        root.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M5,27 C11,23 15,13 27,6 C24,14 24,21 30,27 Z"),
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0x30, p.Accent.R, p.Accent.G, p.Accent.B), 0), new GradientStop(Color.FromArgb(0x10, p.Accent2.R, p.Accent2.G, p.Accent2.B), 1) }
            },
            Stretch = Stretch.Uniform, Width = 520, Height = 420,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -40, -40),
        });

        TextBlock T(string text, double size, FontWeight weight, Color color, double spacing = 0) => new()
        {
            Text = text, FontFamily = inter, FontSize = size, FontWeight = weight, LetterSpacing = spacing,
            Foreground = new SolidColorBrush(color),
        };

        Border Chip(string text) => new()
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(16, 7),
            Background = new SolidColorBrush(Color.FromArgb(0x26, p.Accent.R, p.Accent.G, p.Accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, p.Accent.R, p.Accent.G, p.Accent.B)),
            BorderThickness = new Thickness(1),
            Child = T(text, 15, FontWeight.SemiBold, p.Text),
        };

        var word = T("Slopfin", 96, FontWeight.ExtraBold, p.Text, -3);
        word.VerticalAlignment = VerticalAlignment.Center;

        var chips = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in new[] { "Direct play only · never transcodes", "NostalgiaTV live guide", "Seerr requests", "Intro skipping", "8 themes", "Zero telemetry" })
            chips.Children.Add(Chip(c));

        root.Children.Add(new StackPanel
        {
            Margin = new Thickness(80, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 18,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Children = { Tile(p, 120), word } },
                T("A fast, beautiful Jellyfin client for Windows and Linux, built on mpv.", 24, FontWeight.Medium, p.TextMuted),
                chips,
            }
        });
        root.Children.Add(new Border { BorderBrush = new SolidColorBrush(p.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0) });

        Save(root, w, h, 2, path);
    }
}
