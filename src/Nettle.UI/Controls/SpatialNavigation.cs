using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Nettle.UI.Controls;

/// <summary>
/// D-pad style focus movement (arrow keys / TV remotes / gamepads): moves focus to the nearest
/// focusable control in the pressed direction. This is the foundation for the 10-foot UI on
/// Android TV and works the same with a keyboard on Windows.
/// </summary>
public static class SpatialNavigation
{
    public enum Direction { Left, Right, Up, Down }

    public static bool TryMove(TopLevel top, Direction dir)
    {
        var focused = top.FocusManager?.GetFocusedElement() as Visual;
        var candidates = new List<(InputElement el, Rect rect)>();
        foreach (var v in top.GetVisualDescendants())
        {
            if (v is not InputElement ie) continue;
            if (!ie.Focusable || !ie.IsEffectivelyVisible || !ie.IsEffectivelyEnabled) continue;
            if (v is not (Button or ToggleButton or TextBox or ToggleSwitch or ComboBox or Slider or ListBoxItem)) continue;
            if (ReferenceEquals(v, focused)) continue;
            var r = Bounds(v, top);
            if (r == null || r.Value.Width < 1 || r.Value.Height < 1) continue;
            candidates.Add((ie, r.Value));
        }
        if (candidates.Count == 0) return false;

        if (focused == null || Bounds(focused, top) is not { } from)
        {
            var first = candidates.OrderBy(c => c.rect.Top).ThenBy(c => c.rect.Left).First();
            first.el.Focus(NavigationMethod.Directional);
            return true;
        }

        var fc = from.Center;
        InputElement? best = null;
        double bestScore = double.MaxValue;
        foreach (var (el, r) in candidates)
        {
            var c = r.Center;
            double primary, secondary;
            switch (dir)
            {
                case Direction.Right:
                    if (r.Left < from.Right - 4 && c.X <= fc.X + 1) continue;
                    primary = Math.Max(0, r.Left - from.Right); secondary = Overlap(from.Top, from.Bottom, r.Top, r.Bottom, fc.Y, c.Y); break;
                case Direction.Left:
                    if (r.Right > from.Left + 4 && c.X >= fc.X - 1) continue;
                    primary = Math.Max(0, from.Left - r.Right); secondary = Overlap(from.Top, from.Bottom, r.Top, r.Bottom, fc.Y, c.Y); break;
                case Direction.Down:
                    if (r.Top < from.Bottom - 4 && c.Y <= fc.Y + 1) continue;
                    primary = Math.Max(0, r.Top - from.Bottom); secondary = Overlap(from.Left, from.Right, r.Left, r.Right, fc.X, c.X); break;
                default:
                    if (r.Bottom > from.Top + 4 && c.Y >= fc.Y - 1) continue;
                    primary = Math.Max(0, from.Top - r.Bottom); secondary = Overlap(from.Left, from.Right, r.Left, r.Right, fc.X, c.X); break;
            }
            double score = primary + secondary * 2.5;
            if (score < bestScore) { bestScore = score; best = el; }
        }

        if (best == null) return false;
        best.Focus(NavigationMethod.Directional);
        (best as Control)?.BringIntoView();
        return true;
    }

    /// <summary>0 when the ranges overlap on the cross axis, otherwise the center distance.</summary>
    private static double Overlap(double a1, double a2, double b1, double b2, double ca, double cb) =>
        (b1 < a2 && b2 > a1) ? Math.Abs(ca - cb) * 0.1 : Math.Abs(ca - cb);

    private static Rect? Bounds(Visual v, Visual root)
    {
        var p = v.TranslatePoint(new Point(0, 0), root);
        if (p == null) return null;
        return new Rect(p.Value, v.Bounds.Size);
    }
}
