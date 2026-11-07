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

    public async Task<List<PlaybackSegment>> GetSegmentsAsync(string itemId, CancellationToken ct = default)
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

        if (result.Count == 0)
        {
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
                result.Add(new PlaybackSegment(kind, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
            }
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
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
