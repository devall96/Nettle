using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed record SortOption(string Label, string SortBy, string Order)
{
    public override string ToString() => Label;
}

public sealed record FilterOption(string Label, string? Filters)
{
    public override string ToString() => Label;
}

/// <summary>A choice in one of the extra filters (genre, decade, quality); Value null = no filter.</summary>
public sealed record ChoiceOption(string Label, string? Value)
{
    public override string ToString() => Label;
}

/// <summary>
/// Library grid with paging + row virtualization (only visible rows are realized). Also used
/// for folders / collections and for a person's filmography (everything they appear in).
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    private const int PageSize = 120;
    private const int RandomBatch = 300;
    private readonly BaseItemDto _view;
    private readonly bool _folderMode;
    private readonly string? _personId;
    private readonly List<MediaCardViewModel> _cards = new();
    private int _columns = 6;
    private int _total = int.MaxValue;
    private bool _loadingMore;
    private CancellationTokenSource? _cts;

    public string ViewId => _view.Id;
    public override string Title => _view.Name ?? "Library";
    public override string NavKey => _personId != null ? "person:" + _personId : "lib:" + _view.Id;
    public ObservableCollection<GridRowViewModel> Rows { get; } = new();
    [ObservableProperty] private string _countText = "";

    /// <summary>Asks the view to bring this row into view (A–Z jump).</summary>
    public event Action<int>? ScrollToRow;

    public IReadOnlyList<SortOption> SortOptions { get; } = new List<SortOption>
    {
        new("Name", "SortName", "Ascending"),
        new("Date added", "DateCreated,SortName", "Descending"),
        new("Release date", "PremiereDate,ProductionYear,SortName", "Descending"),
        new("Rating", "CommunityRating,SortName", "Descending"),
        new("Runtime", "Runtime,SortName", "Ascending"),
        new("Random", "Random", "Ascending"),
    };

    public IReadOnlyList<FilterOption> FilterOptions { get; } = new List<FilterOption>
    {
        new("All", null),
        new("Unwatched", "IsUnplayed"),
        new("In progress", "IsResumable"),
        new("Favorites", "IsFavorite"),
    };

    public ObservableCollection<ChoiceOption> GenreOptions { get; } = new() { new("All genres", null) };

    public IReadOnlyList<ChoiceOption> DecadeOptions { get; } = BuildDecades();

    public IReadOnlyList<ChoiceOption> QualityOptions { get; } = new List<ChoiceOption>
    {
        new("Any quality", null),
        new("4K", "4k"),
        new("HD", "hd"),
        new("SD", "sd"),
    };

    /// <summary>"#" then A–Z, shown when sorted by name.</summary>
    public IReadOnlyList<string> Letters { get; } = ["#", .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString())];

    [ObservableProperty] private SortOption _sort;
    [ObservableProperty] private FilterOption _filter;
    [ObservableProperty] private ChoiceOption _genre;
    [ObservableProperty] private ChoiceOption _decade;
    [ObservableProperty] private ChoiceOption _quality;

    public bool ShowLetters => Sort.SortBy == "SortName";
    /// <summary>Genre / decade / quality filters make sense for movie, show and video libraries (and people).</summary>
    public bool ShowExtraFilters => !_folderMode || _view.Type == "BoxSet";

    public bool IsLandscape => _personId == null && (_view.CollectionType is "homevideos" || _folderMode && _view.Type == "Folder");
    public double CardWidth => IsLandscape ? 300 : 164;

    public LibraryViewModel(BaseItemDto view, bool folderMode = false)
    {
        _view = view;
        _folderMode = folderMode;
        _sort = SortOptions[0];
        _filter = FilterOptions[0];
        _genre = GenreOptions[0];
        _decade = DecadeOptions[0];
        _quality = QualityOptions[0];
    }

    private LibraryViewModel(string personId, string name) : this(new BaseItemDto { Id = personId, Name = name, Type = "Person" })
    {
        _personId = personId;
        _sort = SortOptions[2]; // newest first reads best for a filmography
    }

    /// <summary>Everything in the user's libraries that this person is in.</summary>
    public static LibraryViewModel ForPerson(string personId, string name) => new(personId, name);

    partial void OnSortChanged(SortOption value) { OnPropertyChanged(nameof(ShowLetters)); _ = ReloadAsync(); }
    partial void OnFilterChanged(FilterOption value) => _ = ReloadAsync();
    partial void OnGenreChanged(ChoiceOption value) => _ = ReloadAsync();
    partial void OnDecadeChanged(ChoiceOption value) => _ = ReloadAsync();
    partial void OnQualityChanged(ChoiceOption value) => _ = ReloadAsync();

    protected override async Task LoadAsync()
    {
        _ = LoadGenresAsync();
        await ReloadAsync();
    }

    private string? IncludeTypes => _personId != null ? "Movie,Series" : _folderMode ? null : _view.CollectionType switch
    {
        "movies" => "Movie",
        "tvshows" => "Series",
        "boxsets" => "BoxSet",
        "music" => "MusicAlbum",
        "musicvideos" => "MusicVideo",
        "homevideos" => "Video,Photo",
        _ => null
    };

    private async Task LoadGenresAsync()
    {
        if (!ShowExtraFilters) return;
        try
        {
            var names = await AppServices.Jellyfin.GetGenresAsync(_personId == null ? _view.Id : null, IncludeTypes);
            foreach (var n in names) GenreOptions.Add(new ChoiceOption(n, n));
        }
        catch { /* the list just stays at "All genres" */ }
    }

    private static List<ChoiceOption> BuildDecades()
    {
        var list = new List<ChoiceOption> { new("Any year", null) };
        int now = DateTime.Now.Year / 10 * 10;
        for (int d = now; d >= 1920; d -= 10)
            list.Add(new ChoiceOption($"{d}s", string.Join(",", Enumerable.Range(d, 10))));
        list.Add(new ChoiceOption("Before 1920", string.Join(",", Enumerable.Range(1880, 40))));
        return list;
    }

    /// <summary>The current filters as a query (no paging).</summary>
    private ItemQuery BuildQuery() => new()
    {
        ParentId = _personId == null ? _view.Id : null,
        PersonIds = _personId,
        IncludeItemTypes = IncludeTypes,
        Recursive = _personId != null || IncludeTypes != null,
        SortBy = Sort.SortBy,
        SortOrder = Sort.Order,
        Filters = Filter.Filters,
        Genres = Genre.Value,
        Years = Decade.Value,
        Is4K = Quality.Value == "4k" ? true : null,
        IsHd = Quality.Value switch { "hd" => true, "sd" => false, _ => null },
    };

    private async Task ReloadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _cards.Clear();
        Rows.Clear();
        _total = int.MaxValue;
        await RunAsync(() => LoadPageAsync(_cts.Token));
    }

    /// <param name="upTo">Load at least this many items in one request (A–Z jump far down the list).</param>
    private async Task LoadPageAsync(CancellationToken ct, int upTo = 0)
    {
        if (_cards.Count >= _total) return;
        // The server reshuffles on every request, so a second "page" of a random sort would
        // repeat and skip items: random is one larger batch, with no further pages.
        bool random = Sort.SortBy == "Random";
        bool first = _cards.Count == 0;
        var q = BuildQuery();
        q.StartIndex = _cards.Count;
        q.Limit = random ? RandomBatch : Math.Max(PageSize, upTo - _cards.Count + PageSize / 2);
        q.EnableTotalRecordCount = first; // the count is known from the first page
        var res = await AppServices.Jellyfin.GetItemsAsync(q, ct);
        if (ct.IsCancellationRequested) return;
        if (first) _total = res.TotalRecordCount;
        else if (res.Items.Count < q.Limit) _total = _cards.Count + res.Items.Count; // reached the end
        var shape = IsLandscape ? CardShape.Landscape : CardShape.Poster;
        int start = _cards.Count;
        _cards.AddRange(res.Items.Select(i => new MediaCardViewModel(i, shape)));
        AppendRows(start);
        CountText = _total == 0 ? "Nothing matches these filters" : _total == 1 ? "1 item" : $"{_total:N0} items";
        if (random && _cards.Count < _total)
        {
            CountText = $"{_cards.Count:N0} of {_total:N0} items, picked at random";
            _total = _cards.Count; // no more pages
        }
    }

    /// <summary>Called by the view when there's room for a different number of columns.</summary>
    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == _columns && Rows.Count > 0) return;
        _columns = columns;
        Rows.Clear();
        AppendRows(0);
    }

    private void AppendRows(int fromIndex)
    {
        // Re-flow the last partial row, then add new rows.
        int rowStart = (fromIndex / _columns) * _columns;
        while (Rows.Count > rowStart / _columns) Rows.RemoveAt(Rows.Count - 1);
        for (int i = rowStart; i < _cards.Count; i += _columns)
            Rows.Add(new GridRowViewModel(_cards.Skip(i).Take(_columns).ToList()));
    }

    /// <summary>Infinite scroll trigger from the view.</summary>
    public async Task LoadMoreAsync()
    {
        if (_loadingMore || IsBusy || _cards.Count >= _total || _cts == null) return;
        _loadingMore = true;
        try { await RunAsync(() => LoadPageAsync(_cts.Token), showBusy: false); }
        finally { _loadingMore = false; }
    }

    /// <summary>Scrolls to the first title starting with <paramref name="letter"/> (sorted by name).</summary>
    [RelayCommand]
    private async Task JumpTo(string letter)
    {
        if (!ShowLetters || _cts == null) return;
        var ct = _cts.Token;
        await RunAsync(async () =>
        {
            int index = 0;
            if (letter != "#")
            {
                // How many titles sort before this letter = where it starts.
                var q = BuildQuery();
                q.NameLessThan = letter;
                q.Limit = 0;
                index = (await AppServices.Jellyfin.GetItemsAsync(q, ct)).TotalRecordCount;
            }
            if (Sort.Order == "Descending") index = Math.Max(0, _total - index - 1);
            // Everything up to that letter in one request (not page after page).
            if (_cards.Count <= index && _cards.Count < _total && !ct.IsCancellationRequested)
                await LoadPageAsync(ct, upTo: index + 1);
            if (ct.IsCancellationRequested || _cards.Count == 0) return;
            ScrollToRow?.Invoke(Math.Min(index, _cards.Count - 1) / Math.Max(1, _columns));
        }, showBusy: false);
    }

    // ===================== Play all / Shuffle =====================

    /// <summary>The playable things under this page: episodes for shows, otherwise the videos themselves.</summary>
    private string PlayableTypes => _personId != null ? "Movie,Episode"
        : _view.CollectionType switch
        {
            "movies" => "Movie",
            "tvshows" => "Episode",
            "musicvideos" => "MusicVideo",
            "homevideos" => "Video",
            _ => "Movie,Episode,Video,MusicVideo",
        };

    [RelayCommand] private Task PlayAll() => PlayAsync(shuffle: false);
    [RelayCommand] private Task Shuffle() => PlayAsync(shuffle: true);

    private async Task PlayAsync(bool shuffle)
    {
        await RunAsync(async () =>
        {
            var q = BuildQuery();
            q.IncludeItemTypes = PlayableTypes;
            q.Recursive = true;
            q.IsMissing = false;
            if (shuffle) { q.SortBy = "Random"; q.SortOrder = "Ascending"; q.Limit = 300; }
            else
            {
                // Shows play in series order; everything else in the order on screen.
                if (PlayableTypes == "Episode") { q.SortBy = "SeriesSortName,ParentIndexNumber,IndexNumber"; q.SortOrder = "Ascending"; }
                else if (q.SortBy == "Random") { q.SortBy = "SortName"; q.SortOrder = "Ascending"; }
                q.Limit = 500;
            }
            var res = await AppServices.Jellyfin.GetItemsAsync(q);
            var items = res.Items.Where(i => i.IsPlayable && !i.IsFolder).ToList();
            if (items.Count == 0) { AppServices.Main.Toast("Nothing to play here"); return; }
            AppServices.Main.PlayQueue(items);
        }, showBusy: false);
    }
}
