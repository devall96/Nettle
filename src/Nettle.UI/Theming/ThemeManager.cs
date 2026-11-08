using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Nettle.UI.Theming;

/// <summary>
/// A theme is just a palette. Every view uses DynamicResource keys, so switching is instant
/// and adding a theme is one entry below. Palettes are original to Nettle; they take broad
/// inspiration from the moods of popular media apps (dark cinema, OLED black, warm, light)
/// without copying any specific client's design.
/// </summary>
public sealed record ThemePalette(
    string Key, string Name, string Tagline, bool IsLight,
    Color Background, Color BackgroundAlt, Color Surface, Color SurfaceHigh,
    Color Accent, Color Accent2, Color OnAccent,
    Color Text, Color TextMuted, Color Border);

public static class ThemeManager
{
    private static Color C(string hex) => Color.Parse(hex);

    public static IReadOnlyList<ThemePalette> All { get; } = new List<ThemePalette>
    {
        new("midnight", "Midnight", "Deep navy with a violet glow", false,
            C("#0A0D16"), C("#111626"), C("#161C2E"), C("#1F2740"), C("#8B6CFF"), C("#4FB3FF"), C("#FFFFFF"),
            C("#F2F4FA"), C("#9AA3BD"), C("#2A3350")),
        new("obsidian", "Obsidian", "True black for OLED screens", false,
            C("#000000"), C("#07070A"), C("#111114"), C("#1C1C21"), C("#F5F5F7"), C("#A1A1AA"), C("#000000"),
            C("#FFFFFF"), C("#8E8E96"), C("#26262C")),
        new("aurora", "Aurora", "Teal and lime northern lights", false,
            C("#06131A"), C("#0B1C24"), C("#0F2630"), C("#16323E"), C("#2DD4BF"), C("#A3E635"), C("#032127"),
            C("#E8FBF7"), C("#8DB3B0"), C("#1E4250")),
        new("ember", "Ember", "Warm cinema amber", false,
            C("#120C0A"), C("#1A1210"), C("#221815"), C("#2E201B"), C("#FF8A3D"), C("#FF4D6D"), C("#1A0C05"),
            C("#FFF3EC"), C("#BFA49A"), C("#3A2A24")),
        new("sakura", "Sakura", "Soft pink dusk", false,
            C("#140D13"), C("#1C121B"), C("#251824"), C("#322130"), C("#FF7AB6"), C("#B794F6"), C("#2A0A1A"),
            C("#FFF0F7"), C("#C3A3B8"), C("#43293F")),
        new("ocean", "Deep Sea", "Cool blue depths", false,
            C("#07101C"), C("#0C1828"), C("#102036"), C("#172B47"), C("#38BDF8"), C("#22D3EE"), C("#03131F"),
            C("#EAF6FF"), C("#8CA6C2"), C("#1F3A5C")),
        new("forest", "Evergreen", "Mossy greens and gold", false,
            C("#0B110C"), C("#111A13"), C("#17231A"), C("#203024"), C("#7BD88F"), C("#F2C94C"), C("#08200F"),
            C("#EEF7EF"), C("#9DB3A0"), C("#2B4130")),
        new("daylight", "Daylight", "Clean and bright", true,
            C("#F3F4F8"), C("#E9EBF2"), C("#FFFFFF"), C("#F7F8FB"), C("#4258FF"), C("#9B5CFF"), C("#FFFFFF"),
            C("#11131A"), C("#5B6275"), C("#D6DAE5")),
        new("neon", "Neon Nights", "Synthwave magenta and cyan", false,
            C("#0D0717"), C("#140B22"), C("#1B1030"), C("#26173F"), C("#FF4FD8"), C("#3DE8FF"), C("#1A0016"),
            C("#FBEFFF"), C("#B39CC8"), C("#3A2456")),
        new("crimson", "Crimson", "Deep red velvet", false,
            C("#12070A"), C("#1A0B0F"), C("#231016"), C("#30161E"), C("#FF4D5E"), C("#FFB347"), C("#22040A"),
            C("#FFF0F1"), C("#C49AA0"), C("#45212A")),
        new("slate", "Slate", "Calm graphite and steel blue", false,
            C("#111315"), C("#16191C"), C("#1C2024"), C("#252A30"), C("#6FA8FF"), C("#A0AEC0"), C("#0A1424"),
            C("#ECEFF3"), C("#98A2AE"), C("#2E343B")),
        new("mocha", "Mocha", "Coffee browns and cream", false,
            C("#15100D"), C("#1C1612"), C("#251D18"), C("#312620"), C("#D9A26B"), C("#E8D5B5"), C("#241405"),
            C("#F7EEE6"), C("#B8A797"), C("#3D3027")),
        new("phosphor", "Phosphor", "Green-screen retro glow", false,
            C("#050A06"), C("#08100A"), C("#0C170F"), C("#122216"), C("#39FF7A"), C("#B6FF3B"), C("#02150A"),
            C("#DFFFE8"), C("#7FB592"), C("#1A3322")),
        new("contrast", "High Contrast", "Black and yellow, easiest to read", false,
            C("#000000"), C("#000000"), C("#0A0A0A"), C("#161616"), C("#FFE600"), C("#FFFFFF"), C("#000000"),
            C("#FFFFFF"), C("#E6E6E6"), C("#BDBDBD")),
        new("lavender", "Lavender", "Soft lilac, light", true,
            C("#F5F2FB"), C("#ECE6F7"), C("#FFFFFF"), C("#FAF8FE"), C("#7C4DFF"), C("#E0569B"), C("#FFFFFF"),
            C("#1C1528"), C("#6A5F7E"), C("#DCD3EC")),
        new("sand", "Sandstone", "Warm paper tones, light", true,
            C("#F7F2EA"), C("#EFE7DA"), C("#FFFDF9"), C("#FBF7F0"), C("#C2571A"), C("#2F7D6D"), C("#FFFFFF"),
            C("#2A2118"), C("#7A6A58"), C("#E2D6C4")),
    };

    /// <summary>The theme a fresh install starts in (same on Windows, Linux, Android TV and the download page).</summary>
    public const string DefaultKey = "ocean"; // Deep Sea

    public static ThemePalette Default => All.First(t => t.Key == DefaultKey);

    public static ThemePalette Current { get; private set; } = Default;

    public static ThemePalette Find(string? key) => All.FirstOrDefault(t => t.Key == key) ?? Default;

    /// <summary>Raised after a theme is applied (e.g. to recolour the window icon).</summary>
    public static event Action<ThemePalette>? Applied;

    public static void Apply(string? key)
    {
        var p = Find(key);
        Current = p;
        Applied?.Invoke(p);
        var app = Application.Current;
        if (app == null) return;
        var r = app.Resources;

        static SolidColorBrush B(Color c) => new(c);
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        r["BgColor"] = p.Background;
        r["BgBrush"] = B(p.Background);
        r["BgAltBrush"] = B(p.BackgroundAlt);
        r["SurfaceBrush"] = B(p.Surface);
        r["SurfaceHighBrush"] = B(p.SurfaceHigh);
        r["SurfaceTranslucentBrush"] = B(A(p.Surface, 0xE6));
        r["AccentColor"] = p.Accent;
        r["AccentBrush"] = B(p.Accent);
        r["Accent2Brush"] = B(p.Accent2);
        r["AccentSoftBrush"] = B(A(p.Accent, 0x33));
        r["OnAccentBrush"] = B(p.OnAccent);
        r["TextBrush"] = B(p.Text);
        r["TextMutedBrush"] = B(p.TextMuted);
        r["BorderSubtleBrush"] = B(p.Border);
        r["ScrimBrush"] = B(Color.FromArgb(0xB0, 0, 0, 0));

        r["AccentGradientBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(p.Accent, 0), new GradientStop(p.Accent2, 1) }
        };
        r["HeroFadeBottomBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(p.Background, 0), 0.35), new GradientStop(A(p.Background, 0xC0), 0.75), new GradientStop(p.Background, 1) }
        };
        r["HeroFadeLeftBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(p.Background, 0xF0), 0), new GradientStop(A(p.Background, 0x90), 0.4), new GradientStop(A(p.Background, 0), 0.75) }
        };
        r["AmbientGlowBrush"] = new RadialGradientBrush
        {
            Center = new RelativePoint(0.1, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.1, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(p.Accent, 0x22), 0), new GradientStop(A(p.Accent, 0), 1) }
        };

        app.RequestedThemeVariant = p.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        // Feed the accent into Fluent so built-in controls (toggles, sliders, text boxes) match.
        foreach (var style in app.Styles)
        {
            if (style is FluentTheme fluent)
            {
                var variant = p.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
                fluent.Palettes[variant] = new ColorPaletteResources
                {
                    Accent = p.Accent,
                    RegionColor = p.Background,
                    AltHigh = p.Background,
                    BaseHigh = p.Text,
                    ChromeMediumLow = p.Surface,
                    ChromeMedium = p.SurfaceHigh,
                    ChromeLow = p.Surface,
                };
            }
        }
    }
}
