using System.Collections.Concurrent;
using System.Net;
using Slopfin.Core.Api;
using Slopfin.Core.Models;

namespace Slopfin.Core.Services;

/// <summary>
/// NostalgiaTV's schedule names Jellyfin items by id, but Jellyfin re-creates items (new id)
/// when a file is replaced or re-scanned, and NostalgiaTV's index can lag behind. When the id
/// is stale we find the same movie / episode by its metadata and play that instead.
/// </summary>
public sealed class NtvItemResolver
{
    private readonly JellyfinClient _jf;
    private readonly ConcurrentDictionary<string, string> _remap = new();

    public NtvItemResolver(JellyfinClient jf) => _jf = jf;

    /// <summary>Gets playback info for the slot, transparently fixing a stale item id.</summary>
    public async Task<(string itemId, PlaybackInfoResponse info)> GetPlaybackAsync(NtvSlot slot, CancellationToken ct = default)
    {
        slot.TryGetJellyfinItem(out _, out var ntvId);
        var id = _remap.TryGetValue(ntvId, out var mapped) ? mapped : ntvId;
        try
        {
            return (id, await _jf.GetPlaybackInfoAsync(id, null, ct).ConfigureAwait(false));
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var resolved = await FindByMetadataAsync(slot, ct).ConfigureAwait(false)
                           ?? throw new ApiException($"“{slot.DisplayTitle}” isn't in your Jellyfin library under the id NostalgiaTV expects, and no match was found by title.", HttpStatusCode.NotFound);
            _remap[ntvId] = resolved;
            Log.Info($"NTV stale item id {ntvId} → {resolved} ({slot.DisplayTitle})");
            return (resolved, await _jf.GetPlaybackInfoAsync(resolved, null, ct).ConfigureAwait(false));
        }
    }

    private static string Norm(string? s) =>
        new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private async Task<string?> FindByMetadataAsync(NtvSlot slot, CancellationToken ct)
    {
        if (slot.Type == "episode" && slot.SeasonNumber != null && slot.EpisodeNumber != null)
        {
            var show = slot.ShowTitle ?? slot.Title;
            var series = await _jf.GetItemsAsync(new ItemQuery { SearchTerm = show, IncludeItemTypes = "Series", Limit = 20, SortBy = null, SortOrder = null }, ct).ConfigureAwait(false);
            var match = series.Items.FirstOrDefault(s => Norm(s.Name) == Norm(show)) ?? series.Items.FirstOrDefault();
            if (match == null) return null;
            var eps = await _jf.GetEpisodesAsync(match.Id, null, ct).ConfigureAwait(false);
            return eps.Items.FirstOrDefault(e => e.ParentIndexNumber == slot.SeasonNumber && e.IndexNumber == slot.EpisodeNumber)?.Id;
        }

        var title = slot.Title ?? slot.ShowTitle;
        if (string.IsNullOrEmpty(title)) return null;
        var res = await _jf.GetItemsAsync(new ItemQuery { SearchTerm = title, IncludeItemTypes = "Movie,Video,MusicVideo", Limit = 30, SortBy = null, SortOrder = null }, ct).ConfigureAwait(false);
        var named = res.Items.Where(i => Norm(i.Name) == Norm(title)).ToList();
        var best = named.FirstOrDefault(i => slot.Year == null || i.ProductionYear == slot.Year)
                   ?? named.FirstOrDefault(i => slot.Year != null && i.ProductionYear != null && Math.Abs(i.ProductionYear.Value - slot.Year.Value) <= 1)
                   ?? (named.Count == 1 ? named[0] : null);
        return best?.Id;
    }
}
