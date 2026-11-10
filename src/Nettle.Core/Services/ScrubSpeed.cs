namespace Nettle.Core.Services;

/// <summary>
/// How fast hold-to-seek moves (holding ← / → or the remote's Left / Right). It ramps with how long the key
/// has been held, not with how many key repeats arrived, so it stays controllable: 30× at first, doubling
/// every second, capped so a whole video takes about <see cref="CrossSeconds"/> of holding.
/// Same numbers as the Android TV app (core/services/ScrubSpeed.kt).
/// </summary>
public static class ScrubSpeed
{
    /// <summary>Video seconds per second held at the start.</summary>
    public const double Start = 30;
    /// <summary>Holding this long at full speed crosses the whole video.</summary>
    public const double CrossSeconds = 8;

    /// <summary>Video seconds per second of holding, after <paramref name="heldMs"/>, for a video <paramref name="lengthMs"/> long.</summary>
    public static double Rate(double heldMs, double lengthMs)
    {
        var cap = Math.Max(Start, lengthMs / 1000 / CrossSeconds);
        return Math.Min(cap, Start * Math.Pow(2, Math.Max(0, heldMs) / 1000));
    }

    /// <summary>How far (ms) to move for a key repeat <paramref name="sinceLastMs"/> after the previous one.</summary>
    public static double AdvanceMs(double heldMs, double sinceLastMs, double lengthMs) =>
        Rate(heldMs, lengthMs) * Math.Clamp(sinceLastMs, 0, 500);
}
