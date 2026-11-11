using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Api;

namespace Nettle.UI.ViewModels;

/// <summary>A Seerr search hit that isn't in the library yet; opens it on the Requests page.</summary>
public sealed partial class SeerrSuggestionViewModel(Nettle.Core.Models.SeerrResult r)
{
    public string Title => r.DisplayTitle;
    public string Subtitle => $"{(r.MediaType == "tv" ? "Series" : "Movie")}{(r.Year != null ? "  •  " + r.Year : "")}";
    public string? PosterUrl => AppServices.Seerr.ImageUrl(r.PosterPath, 342);
    public string Status => Nettle.Core.Models.SeerrMediaStatus.Describe(r.MediaInfo?.Status) is { Length: > 0 } s ? s : "Not in your library";

    [RelayCommand]
    private void Open() => AppServices.Main.Shell?.NavigateRoot(new RequestsViewModel { SearchText = r.DisplayTitle });
}

public sealed partial class SearchViewModel : PageViewModel
{
    /// <summary>Matching titles from Seerr that aren't available in the library (requestable).</summary>
    public ObservableCollection<SeerrSuggestionViewModel> SeerrSuggestions { get; } = new();
    [ObservableProperty] private bool _hasSeerrSuggestions;

    public override string NavKey => "search";
    public ObservableCollection<CardRowViewModel> Results { get; } = new();
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _noResults;
    private CancellationTokenSource? _cts;

    private const int PerKind = 24;
    private static readonly (string Title, string Type, CardShape Shape)[] Kinds =
    [
        ("Movies", "Movie", CardShape.Poster),
        ("Shows", "Series", CardShape.Poster),
        ("Episodes", "Episode", CardShape.Landscape),
        ("Collections", "BoxSet", CardShape.Poster),
        ("Music videos", "MusicVideo", CardShape.Landscape),
        ("Videos", "Video", CardShape.Landscape),
    ];

    public bool SeerrAvailable => AppServices.Seerr.IsConfigured;

    partial void OnQueryChanged(string value) => _ = DebouncedSearchAsync(value);

    protected override Task LoadAsync() => string.IsNullOrWhiteSpace(Query) ? Task.CompletedTask : DebouncedSearchAsync(Query, 0);

    private async Task DebouncedSearchAsync(string text, int delay = 280)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { await Task.Delay(delay, cts.Token); } catch (TaskCanceledException) { return; }
        if (string.IsNullOrWhiteSpace(text))
        {
            Results.Clear();
            SeerrSuggestions.Clear();
            HasSeerrSuggestions = false;
            NoResults = false;
            return;
        }

        _ = SearchSeerrAsync(text.Trim(), cts.Token);
        await RunAsync(async () =>
        {
            // One query per kind, in parallel: a single mixed query lets a common word fill the
            // whole limit with episodes and push every movie and show out of the results.
            var jf = AppServices.Jellyfin;
            var term = text.Trim();
            var rows = Kinds.Select(k => (kind: k, task: jf.GetItemsAsync(new ItemQuery
            {
                SearchTerm = term,
                IncludeItemTypes = k.Type,
                Recursive = true,
                SortBy = null,
                SortOrder = null,
                Limit = PerKind,
            }, cts.Token))).ToList();

            var built = new List<CardRowViewModel>();
            ApiException? failure = null;
            int failed = 0;
            foreach (var (kind, task) in rows)
            {
                Nettle.Core.Models.QueryResult<Nettle.Core.Models.BaseItemDto> res;
                try { res = await task; }
                catch (ApiException ex) { failure = ex; failed++; continue; } // one kind failing shouldn't hide the others
                catch (OperationCanceledException) { continue; } // a newer search took over; still await the rest
                var items = res.Items.Where(i => i.Type == kind.Type).Select(i => new MediaCardViewModel(i, kind.Shape, true)).ToList();
                if (items.Count > 0) built.Add(new CardRowViewModel(kind.Title, items));
            }
            if (cts.IsCancellationRequested) return;
            if (failed == rows.Count && failure != null) throw failure; // server unreachable: show the error, not "no results"

            Results.Clear();
            foreach (var r in built) Results.Add(r);
            NoResults = Results.Count == 0;
        }, showBusy: true);
    }

    /// <summary>Seerr's matches that aren't available yet, shown under the library results (only when Seerr is set up).</summary>
    private async Task SearchSeerrAsync(string term, CancellationToken ct)
    {
        await AppServices.SeerrReady;
        if (!AppServices.Seerr.IsConfigured) return;
        try
        {
            var page = await AppServices.Seerr.SearchAsync(term, 1, ct);
            if (ct.IsCancellationRequested) return;
            var hits = page.Results
                .Where(r => r.MediaType is "movie" or "tv" && r.MediaInfo?.Status is not Nettle.Core.Models.SeerrMediaStatus.Available)
                .Take(12)
                .Select(r => new SeerrSuggestionViewModel(r))
                .ToList();
            SeerrSuggestions.Clear();
            foreach (var h in hits) SeerrSuggestions.Add(h);
            HasSeerrSuggestions = SeerrSuggestions.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Nettle.Core.Services.Log.Info("Seerr search failed: " + ex.Message); }
    }

    [RelayCommand]
    private void RequestIt()
    {
        var req = new RequestsViewModel { SearchText = Query };
        AppServices.Main.Shell?.NavigateRoot(req);
    }
}
