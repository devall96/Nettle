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
///
/// Rules, as on TV launchers:
///  • Left / Right stay within the current row (the target must overlap it vertically).
///  • Left from the page into the navigation rail lands on the rail's current page;
///    Right out of the rail goes back to where you were on the page.
///  • With nothing sensible focused yet, the page's main action (a primary button) is picked.
/// </summary>
public static class SpatialNavigation
{
    public enum Direction { Left, Right, Up, Down }

    /// <summary>Last focused control on the page (outside the rail), to return to from the rail.</summary>
    private static WeakReference<InputElement>? _lastInPage;

    /// <summary>A new page is showing and the user hasn't moved focus on it yet.</summary>
    private static bool _freshPage = true;

    /// <summary>Called when the shell shows another page: its main action gets focus once it's there.</summary>
    public static void PageChanged() { _freshPage = true; _lastInPage = null; }

    /// <summary>Per page: the data item of the control last focused there, to come back to it.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _pageFocus = new();

    private static object? PageOf(Visual v) =>
        v.GetVisualAncestors().OfType<TransitioningContentControl>().FirstOrDefault(t => t.Name == "PageHost")?.Content;

    public static bool TryMove(TopLevel top, Direction dir)
    {
        _freshPage = false;
        var focused = top.FocusManager?.GetFocusedElement() as Visual;
        bool focusedIsTarget = focused != null && IsTarget(focused);
        var candidates = new List<(InputElement el, Rect rect)>();
        foreach (var v in top.GetVisualDescendants())
        {
            if (v is not InputElement ie || !IsTarget(v)) continue;
            if (!ie.Focusable || !ie.IsEffectivelyVisible || !ie.IsEffectivelyEnabled) continue;
            if (ReferenceEquals(v, focused)) continue;
            var r = Bounds(v, top);
            if (r == null || r.Value.Width < 1 || r.Value.Height < 1) continue;
            candidates.Add((ie, r.Value));
        }
        if (candidates.Count == 0) return false;

        if (!focusedIsTarget || Bounds(focused!, top) is not { } from)
            return Go(PickEntry(top, candidates));

        bool fromRail = InRail(focused!);
        if (!fromRail && focused is InputElement fe) _lastInPage = new WeakReference<InputElement>(fe);

        // Out of the rail to the right: back to where the user was on the page.
        if (fromRail && dir == Direction.Right && _lastInPage?.TryGetTarget(out var back) == true
            && back.IsEffectivelyVisible && back.IsEffectivelyEnabled && (back as Visual)?.GetVisualRoot() == top)
            return Go(back);
        if (fromRail && dir == Direction.Right && candidates.Any(c => !InRail(c.el)))
            return Go(PickEntry(top, candidates));

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
                    if (!(r.Top < from.Bottom && r.Bottom > from.Top)) continue; // same row only
                    primary = Math.Max(0, r.Left - from.Right); secondary = Math.Abs(fc.Y - c.Y) * 0.1; break;
                case Direction.Left:
                    if (r.Right > from.Left + 4 && c.X >= fc.X - 1) continue;
                    if (!(r.Top < from.Bottom && r.Bottom > from.Top) && !(InRail(el) && !fromRail)) continue;
                    primary = Math.Max(0, from.Left - r.Right); secondary = Math.Abs(fc.Y - c.Y) * 0.1; break;
                case Direction.Down:
                    if (r.Top < from.Bottom - 4 && c.Y <= fc.Y + 1) continue;
                    primary = Math.Max(0, r.Top - from.Bottom); secondary = Overlap(from.Left, from.Right, r.Left, r.Right, fc.X, c.X); break;
                default:
                    if (r.Bottom > from.Top + 4 && c.Y >= fc.Y - 1) continue;
                    primary = Math.Max(0, from.Top - r.Bottom); secondary = Overlap(from.Left, from.Right, r.Left, r.Right, fc.X, c.X); break;
            }
            // Up / Down never wander between the rail and the page.
            if (dir is Direction.Up or Direction.Down && InRail(el) != fromRail) continue;
            double score = primary + secondary * 2.5;
            if (score < bestScore) { bestScore = score; best = el; }
        }

        if (best == null) return false;
        // Into the rail: land on the page that's showing, not whatever item happens to be level.
        if (!fromRail && InRail(best) && candidates.FirstOrDefault(c => InRail(c.el) && (c.el as Control)?.Classes.Contains("selected") == true).el is { } current)
            best = current;
        return Go(best);
    }

    /// <summary>
    /// TV: when focus sits on nothing the remote can act on (a page container, or nothing at all),
    /// put it on the page's main action. Returns true if focus was moved.
    /// </summary>
    public static bool EnsureFocus(TopLevel top)
    {
        // Keep a focus the user chose; on a fresh page, also move it off the rail (where Avalonia
        // puts first focus) onto the page.
        if (top.FocusManager?.GetFocusedElement() is Visual f && IsTarget(f) && f.IsEffectivelyVisible && !(_freshPage && InRail(f))) return false;
        var candidates = new List<(InputElement el, Rect rect)>();
        foreach (var v in top.GetVisualDescendants())
        {
            if (v is not InputElement ie || !IsTarget(v) || !ie.Focusable || !ie.IsEffectivelyVisible || !ie.IsEffectivelyEnabled) continue;
            if (Bounds(v, top) is { Width: >= 1, Height: >= 1 } r) candidates.Add((ie, r));
        }
        if (candidates.Count == 0) return false;
        // Back on a page seen before: return to the item that was focused there.
        if (_freshPage && candidates.Select(c => PageOf((Visual)c.el)).FirstOrDefault(p => p != null) is { } page
            && _pageFocus.TryGetValue(page, out var item)
            && candidates.FirstOrDefault(c => !InRail(c.el) && ReferenceEquals((c.el as Control)?.DataContext, item)).el is { } previous)
            return Go(previous);
        return Go(PickEntry(top, candidates));
    }

    private static bool Go(InputElement el)
    {
        el.Focus(NavigationMethod.Directional);
        if (el is Control { DataContext: { } dc } ctl && PageOf(ctl) is { } page && !ReferenceEquals(dc, page))
            _pageFocus.AddOrUpdate(page, dc);
        if (el is Control c) ScrollIntoView(c);
        return true;
    }

    /// <summary>
    /// Scrolls every scroll viewer around <paramref name="c"/> so it's fully on screen with some room
    /// around it (rows keep the next card peeking in). Avalonia's own bring-into-view is turned off on
    /// TV: with cards lifting on focus it often scrolled the focused card off screen.
    /// </summary>
    public static void ScrollIntoView(Control c)
    {
        foreach (var sv in c.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (c.TranslatePoint(new Point(0, 0), sv) is not { } p) continue;
            // Use the layout size: the focus lift (a render transform) shouldn't move the page.
            var size = c.Bounds.Size;
            var view = sv.Viewport;
            var offset = sv.Offset;
            double x = offset.X, y = offset.Y;
            const double marginX = 48, marginY = 56;
            if (sv.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled && view.Width > 0)
            {
                if (p.X < marginX) x += p.X - marginX;
                else if (p.X + size.Width > view.Width - marginX) x += p.X + size.Width - (view.Width - marginX);
            }
            if (sv.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled && view.Height > 0)
            {
                if (p.Y < marginY) y += p.Y - marginY;
                else if (p.Y + size.Height > view.Height - marginY) y += p.Y + size.Height - (view.Height - marginY);
            }
            x = Math.Clamp(x, 0, Math.Max(0, sv.Extent.Width - view.Width));
            y = Math.Clamp(y, 0, Math.Max(0, sv.Extent.Height - view.Height));
            if (x != offset.X || y != offset.Y) sv.Offset = new Vector(x, y);
        }
    }

    /// <summary>First focus on a page: its primary action if one is on screen, else the top-left control of the page.</summary>
    private static InputElement PickEntry(TopLevel top, List<(InputElement el, Rect rect)> candidates)
    {
        var screen = new Rect(top.Bounds.Size);
        var onScreen = candidates.Where(c => screen.Intersects(c.rect) && !InRail(c.el)).ToList();
        return onScreen.FirstOrDefault(c => (c.el as Control)?.Classes.Contains("primary") == true).el
            ?? onScreen.OrderBy(c => c.rect.Top).ThenBy(c => c.rect.Left).Select(c => c.el).FirstOrDefault()
            ?? candidates.OrderBy(c => c.rect.Top).ThenBy(c => c.rect.Left).First().el;
    }

    private static bool IsTarget(Visual v) => v is Button or ToggleButton or TextBox or ToggleSwitch or ComboBox or Slider or ListBoxItem;

    private static bool InRail(Visual v) => v.GetVisualAncestors().Any(a => a is Border { Name: "Rail" });

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
