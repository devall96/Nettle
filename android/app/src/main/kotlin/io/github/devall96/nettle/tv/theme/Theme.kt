package io.github.devall96.nettle.tv.theme

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ShaderBrush
import androidx.compose.ui.graphics.Shader
import androidx.compose.ui.graphics.RadialGradientShader
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.sp
import io.github.devall96.nettle.tv.R

/**
 * A theme is just a palette (port of Nettle.UI.Theming.ThemeManager). The colours are copied
 * exactly from the Windows app; the derived brushes use the same stops.
 */
@Immutable
data class ThemePalette(
    val key: String, val name: String, val tagline: String, val isLight: Boolean,
    val background: Color, val backgroundAlt: Color, val surface: Color, val surfaceHigh: Color,
    val accent: Color, val accent2: Color, val onAccent: Color,
    val text: Color, val textMuted: Color, val border: Color,
) {
    val surfaceTranslucent = surface.copy(alpha = 0xE6 / 255f)
    val accentSoft = accent.copy(alpha = 0x33 / 255f)
    val scrim = Color(0xB0000000)
    val danger = Color(0xFFFF6B6B)
    val warning = Color(0xFFFCD34D)

    /** Accent → Accent2, top-left to bottom-right (Avalonia's relative 0,0 → 1,1). */
    val accentGradient: Brush = object : ShaderBrush() {
        override fun createShader(size: Size): Shader =
            androidx.compose.ui.graphics.LinearGradientShader(Offset.Zero, Offset(size.width, size.height), listOf(accent, accent2))
    }

    /** Bottom fade of heroes and backdrops. */
    val heroFadeBottom: Brush = Brush.verticalGradient(
        0.35f to background.copy(alpha = 0f), 0.75f to background.copy(alpha = 0xC0 / 255f), 1f to background,
    )

    /** Left fade of heroes and backdrops (text side). */
    val heroFadeLeft: Brush = Brush.horizontalGradient(
        0f to background.copy(alpha = 0xF0 / 255f), 0.4f to background.copy(alpha = 0x90 / 255f), 0.75f to background.copy(alpha = 0f),
    )

    /** Soft glow behind everything: centre (10%, 0), an ellipse 90% of the width by 90% of the height (as Avalonia's relative radii). */
    val ambientGlow: Brush = object : ShaderBrush() {
        override fun createShader(size: Size): Shader {
            val rx = size.width * 0.9f
            val s = RadialGradientShader(Offset.Zero, rx, listOf(accent.copy(alpha = 0x22 / 255f), accent.copy(alpha = 0f)))
            s.setLocalMatrix(android.graphics.Matrix().apply {
                setScale(1f, size.height / size.width)
                postTranslate(size.width * 0.1f, 0f)
            })
            return s
        }
    }

    /**
     * [background] with [ambientGlow] on top, as ONE opaque paint: the glow's colours are blended
     * over the background up front (same pixels, half the fill work on the TV's small GPU).
     */
    val backgroundWithGlow: Brush = object : ShaderBrush() {
        override fun createShader(size: Size): Shader {
            val rx = size.width * 0.9f
            val a = 0x22 / 255f
            val inner = Color(
                background.red + (accent.red - background.red) * a,
                background.green + (accent.green - background.green) * a,
                background.blue + (accent.blue - background.blue) * a,
            )
            val s = RadialGradientShader(Offset.Zero, rx, listOf(inner, background))
            s.setLocalMatrix(android.graphics.Matrix().apply {
                setScale(1f, size.height / size.width)
                postTranslate(size.width * 0.1f, 0f)
            })
            return s
        }
    }
}

private fun c(hex: String): Color = Color(android.graphics.Color.parseColor(hex))

object Themes {
    val all: List<ThemePalette> = listOf(
        ThemePalette("midnight", "Midnight", "Deep navy with a violet glow", false,
            c("#0A0D16"), c("#111626"), c("#161C2E"), c("#1F2740"), c("#8B6CFF"), c("#4FB3FF"), c("#FFFFFF"),
            c("#F2F4FA"), c("#9AA3BD"), c("#2A3350")),
        ThemePalette("obsidian", "Obsidian", "True black for OLED screens", false,
            c("#000000"), c("#07070A"), c("#111114"), c("#1C1C21"), c("#F5F5F7"), c("#A1A1AA"), c("#000000"),
            c("#FFFFFF"), c("#8E8E96"), c("#26262C")),
        ThemePalette("aurora", "Aurora", "Teal and lime northern lights", false,
            c("#06131A"), c("#0B1C24"), c("#0F2630"), c("#16323E"), c("#2DD4BF"), c("#A3E635"), c("#032127"),
            c("#E8FBF7"), c("#8DB3B0"), c("#1E4250")),
        ThemePalette("ember", "Ember", "Warm cinema amber", false,
            c("#120C0A"), c("#1A1210"), c("#221815"), c("#2E201B"), c("#FF8A3D"), c("#FF4D6D"), c("#1A0C05"),
            c("#FFF3EC"), c("#BFA49A"), c("#3A2A24")),
        ThemePalette("sakura", "Sakura", "Soft pink dusk", false,
            c("#140D13"), c("#1C121B"), c("#251824"), c("#322130"), c("#FF7AB6"), c("#B794F6"), c("#2A0A1A"),
            c("#FFF0F7"), c("#C3A3B8"), c("#43293F")),
        ThemePalette("ocean", "Deep Sea", "Cool blue depths", false,
            c("#07101C"), c("#0C1828"), c("#102036"), c("#172B47"), c("#38BDF8"), c("#22D3EE"), c("#03131F"),
            c("#EAF6FF"), c("#8CA6C2"), c("#1F3A5C")),
        ThemePalette("forest", "Evergreen", "Mossy greens and gold", false,
            c("#0B110C"), c("#111A13"), c("#17231A"), c("#203024"), c("#7BD88F"), c("#F2C94C"), c("#08200F"),
            c("#EEF7EF"), c("#9DB3A0"), c("#2B4130")),
        ThemePalette("daylight", "Daylight", "Clean and bright", true,
            c("#F3F4F8"), c("#E9EBF2"), c("#FFFFFF"), c("#F7F8FB"), c("#4258FF"), c("#9B5CFF"), c("#FFFFFF"),
            c("#11131A"), c("#5B6275"), c("#D6DAE5")),
        ThemePalette("neon", "Neon Nights", "Synthwave magenta and cyan", false,
            c("#0D0717"), c("#140B22"), c("#1B1030"), c("#26173F"), c("#FF4FD8"), c("#3DE8FF"), c("#1A0016"),
            c("#FBEFFF"), c("#B39CC8"), c("#3A2456")),
        ThemePalette("crimson", "Crimson", "Deep red velvet", false,
            c("#12070A"), c("#1A0B0F"), c("#231016"), c("#30161E"), c("#FF4D5E"), c("#FFB347"), c("#22040A"),
            c("#FFF0F1"), c("#C49AA0"), c("#45212A")),
        ThemePalette("slate", "Slate", "Calm graphite and steel blue", false,
            c("#111315"), c("#16191C"), c("#1C2024"), c("#252A30"), c("#6FA8FF"), c("#A0AEC0"), c("#0A1424"),
            c("#ECEFF3"), c("#98A2AE"), c("#2E343B")),
        ThemePalette("mocha", "Mocha", "Coffee browns and cream", false,
            c("#15100D"), c("#1C1612"), c("#251D18"), c("#312620"), c("#D9A26B"), c("#E8D5B5"), c("#241405"),
            c("#F7EEE6"), c("#B8A797"), c("#3D3027")),
        ThemePalette("phosphor", "Phosphor", "Green-screen retro glow", false,
            c("#050A06"), c("#08100A"), c("#0C170F"), c("#122216"), c("#39FF7A"), c("#B6FF3B"), c("#02150A"),
            c("#DFFFE8"), c("#7FB592"), c("#1A3322")),
        ThemePalette("contrast", "High Contrast", "Black and yellow, easiest to read", false,
            c("#000000"), c("#000000"), c("#0A0A0A"), c("#161616"), c("#FFE600"), c("#FFFFFF"), c("#000000"),
            c("#FFFFFF"), c("#E6E6E6"), c("#BDBDBD")),
        ThemePalette("lavender", "Lavender", "Soft lilac, light", true,
            c("#F5F2FB"), c("#ECE6F7"), c("#FFFFFF"), c("#FAF8FE"), c("#7C4DFF"), c("#E0569B"), c("#FFFFFF"),
            c("#1C1528"), c("#6A5F7E"), c("#DCD3EC")),
        ThemePalette("sand", "Sandstone", "Warm paper tones, light", true,
            c("#F7F2EA"), c("#EFE7DA"), c("#FFFDF9"), c("#FBF7F0"), c("#C2571A"), c("#2F7D6D"), c("#FFFFFF"),
            c("#2A2118"), c("#7A6A58"), c("#E2D6C4")),
    )

    fun find(key: String?): ThemePalette = all.firstOrNull { it.key == key } ?: all[0]
}

/** Inter, the same font files the Windows app uses (Avalonia.Fonts.Inter). */
val Inter = FontFamily(
    Font(R.font.inter_light, FontWeight.Light),
    Font(R.font.inter_regular, FontWeight.Normal),
    Font(R.font.inter_medium, FontWeight.Medium),
    Font(R.font.inter_semibold, FontWeight.SemiBold),
    Font(R.font.inter_bold, FontWeight.Bold),
)

/** Text styles from Controls.axaml (sizes in Nettle units). */
@Immutable
data class NettleType(
    val body: TextStyle,
    val display: TextStyle,
    val h1: TextStyle,
    val h2: TextStyle,
    val h3: TextStyle,
    val caption: TextStyle,
    val section: TextStyle,
)

private val base = TextStyle(
    fontFamily = Inter, fontSize = 14.sp,
    platformStyle = PlatformTextStyle(includeFontPadding = false),
    lineHeightStyle = LineHeightStyle(LineHeightStyle.Alignment.Center, LineHeightStyle.Trim.None),
)

val NettleTypography = NettleType(
    body = base,
    display = base.copy(fontSize = 52.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-1).sp),
    h1 = base.copy(fontSize = 36.sp, fontWeight = FontWeight.Bold, letterSpacing = (-0.5).sp),
    h2 = base.copy(fontSize = 26.sp, fontWeight = FontWeight.Bold),
    h3 = base.copy(fontSize = 19.sp, fontWeight = FontWeight.SemiBold),
    caption = base.copy(fontSize = 12.5.sp),
    section = base.copy(fontSize = 13.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.2.sp),
)

val LocalPalette = staticCompositionLocalOf { Themes.all[0] }
val LocalType = staticCompositionLocalOf { NettleTypography }
/** False when Settings → Reduce motion is on. */
val LocalMotion = staticCompositionLocalOf { true }

object Nettle {
    val colors: ThemePalette @Composable get() = LocalPalette.current
    val type: NettleType @Composable get() = LocalType.current
    val motion: Boolean @Composable get() = LocalMotion.current
}

/**
 * Draws the app on a fixed canvas [canvasWidth] "Nettle units" wide (1440 = the Windows app's
 * default window), so every size from the Windows .axaml files is used verbatim as dp and the TV
 * shows the same proportions, whatever its own density.
 */
@Composable
fun NettleTheme(palette: ThemePalette, canvasWidth: Int, motion: Boolean, content: @Composable () -> Unit) {
    val cfg = LocalConfiguration.current
    val real = LocalDensity.current
    val density = remember(cfg.screenWidthDp, real.density, canvasWidth) {
        val widthPx = cfg.screenWidthDp * real.density
        Density(density = widthPx / canvasWidth, fontScale = 1f)
    }
    CompositionLocalProvider(
        LocalDensity provides density,
        LocalPalette provides palette,
        LocalType provides NettleTypography,
        LocalMotion provides motion,
        content = content,
    )
}
