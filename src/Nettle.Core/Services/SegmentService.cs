using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.Core.Services;

public enum SegmentKind { Intro, Recap, Credits, Preview, Commercial }

public sealed record PlaybackSegment(SegmentKind Kind, TimeSpan Start, TimeSpan End)
{
    public string SkipLabel => Kind switch
    {
        SegmentKind.Intro => "Skip Intro",
        SegmentKind.Recap => "Skip Recap",
        SegmentKind.Credits => "Skip Credits",
        SegmentKind.Preview => "Skip Preview",
        SegmentKind.Commercial => "Skip Ad Break",
        _ => "Skip"
    };
}

/// <summary>
/// Collects skippable segments from server plugins. Prefers Jellyfin's Media Segments API
/// (10.10+, filled by Intro Skipper / chapter-based segment plugins) and falls back to the
/// Intro Skipper plugin's legacy endpoint.
/// </summary>
public sealed class SegmentService
{
    private readonly JellyfinClient _client;
    public SegmentService(JellyfinClient client) => _client = client;

    /// <param name="legacyFallback">Also ask the Intro Skipper plugin's old endpoint (it only knows episodes).</param>
    public async Task<List<PlaybackSegment>> GetSegmentsAsync(string itemId, bool legacyFallback = true, CancellationToken ct = default)
    {
        var result = new List<PlaybackSegment>();
        var segs = await _client.GetMediaSegmentsAsync(itemId, ct).ConfigureAwait(false);
        foreach (var s in segs)
        {
            SegmentKind? kind = s.Type switch
            {
                "Intro" => SegmentKind.Intro,
                "Recap" => SegmentKind.Recap,
                "Outro" => SegmentKind.Credits,
                "Preview" => SegmentKind.Preview,
                "Commercial" => SegmentKind.Commercial,
                _ => null
            };
            if (kind != null && s.EndTicks > s.StartTicks)
                result.Add(new PlaybackSegment(kind.Value, TimeSpan.FromTicks(s.StartTicks), TimeSpan.FromTicks(s.EndTicks)));
        }

        // The plugin's own endpoint is asked whenever the server's list has no intro: some setups publish
        // only part of what Intro Skipper found (credits but no intro), which used to hide Skip Intro.
        if (legacyFallback && result.All(s => s.Kind != SegmentKind.Intro))
        {
            var have = result.Select(s => s.Kind).ToHashSet();
            var legacy = await _client.GetIntroSkipperSegmentsAsync(itemId, ct).ConfigureAwait(false);
            foreach (var (key, v) in legacy)
            {
                if (!v.Valid) continue;
                double start = v.IntroEnd > 0 ? v.IntroStart : v.Start;
                double end = v.IntroEnd > 0 ? v.IntroEnd : v.End;
                if (end <= start) continue;
                var kind = key switch
                {
                    "Introduction" => SegmentKind.Intro,
                    "Credits" => SegmentKind.Credits,
                    "Recap" => SegmentKind.Recap,
                    "Preview" => SegmentKind.Preview,
                    "Commercial" => SegmentKind.Commercial,
                    _ => SegmentKind.Intro
                };
                if (have.Contains(kind)) continue; // the server's own entry wins
                result.Add(new PlaybackSegment(kind, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
            }
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>
    /// <see cref="GetSegmentsAsync"/>, asked again (after 3 s, then 10 s) when the server couldn't answer.
    /// One failed request at the start of an episode used to mean no Skip buttons for all of it.
    /// </summary>
    public async Task<List<PlaybackSegment>> GetSegmentsReliablyAsync(string itemId, bool legacyFallback = true, CancellationToken ct = default)
    {
        int[] waits = [3000, 10000];
        for (int attempt = 0; ; attempt++)
        {
            try { return await GetSegmentsAsync(itemId, legacyFallback, ct).ConfigureAwait(false); }
            catch (ApiException ex)
            {
                if (attempt >= waits.Length) { Log.Warn("skip segments couldn't be loaded: " + ex.Message); return new(); }
                Log.Info($"skip segments not loaded ({ex.Message}); asking again");
                await Task.Delay(waits[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The segment to act on at <paramref name="position"/>: the first one covering it that isn't switched
    /// off or dismissed. (Taking simply the first one let a hidden recap mask the intro it overlaps.)
    /// </summary>
    public static PlaybackSegment? ActiveAt(IReadOnlyList<PlaybackSegment> segments, TimeSpan position, PlaybackSettings settings, PlaybackSegment? dismissed)
    {
        foreach (var s in segments)
        {
            if (position < s.Start || position >= s.End - TimeSpan.FromSeconds(0.5)) continue;
            if (s == dismissed || ActionFor(s.Kind, settings) == SegmentAction.Off) continue;
            return s;
        }
        return null;
    }

    public static SegmentAction ActionFor(SegmentKind kind, PlaybackSettings s) => kind switch
    {
        SegmentKind.Intro => s.IntroAction,
        SegmentKind.Recap => s.RecapAction,
        SegmentKind.Credits => s.CreditsAction,
        SegmentKind.Preview => s.PreviewAction,
        SegmentKind.Commercial => s.CommercialAction,
        _ => SegmentAction.ShowButton
    };
}
