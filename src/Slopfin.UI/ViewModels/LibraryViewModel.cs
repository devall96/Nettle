using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Api;
using Slopfin.Core.Models;

namespace Slopfin.UI.ViewModels;

public sealed record SortOption(string Label, string SortBy, string Order)
{
    public override string ToString() => Label;
}

public sealed record FilterOption(string Label, string? Filters)
{
    public override string ToString() => Label;
}

/// <summary>Library grid with paging + row virtualization (only visible rows are realized).</summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    private const int PageSize = 120;
    private readonly BaseItemDto _view;
    private readonly bool _folderMode;
    private readonly List<MediaCardViewModel> _cards = new();
    private int _columns = 6;
    private int _total = int.MaxValue;
    private bool _loadingMore;
    private CancellationTokenSource? _cts;

    public string ViewId => _view.Id;
    public override string Title => _view.Name ?? "Library";
    public override string NavKey => "lib:" + _view.Id;
    public ObservableCollection<GridRowViewModel> Rows { get; } = new();
    [ObservableProperty] private string _countText = "";

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

    [ObservableProperty] private SortOption _sort;
    [ObservableProperty] private FilterOption _filter;

    public bool IsLandscape => _view.CollectionType is "homevideos" || _folderMode && _view.Type == "Folder";
    public double CardWidth => IsLandscape ? 300 : 164;

    public LibraryViewModel(BaseItemDto view, bool folderMode = false)
    {
        _view = view;
        _folderMode = folderMode;
        _sort = SortOptions[0];
        _filter = FilterOptions[0];
    }

    partial void OnSortChanged(SortOption value) => _ = ReloadAsync();
    partial void OnFilterChanged(FilterOption value) => _ = ReloadAsync();

    protected override Task LoadAsync() => ReloadAsync();

    private string? IncludeTypes => _folderMode ? null : _view.CollectionType switch
    {
        "movies" => "Movie",
        "tvshows" => "Series",
        "boxsets" => "BoxSet",
        "music" => "MusicAlbum",
        "musicvideos" => "MusicVideo",
        "homevideos" => "Video,Photo",
        _ => null
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

    private async Task LoadPageAsync(CancellationToken ct)
    {
        if (_cards.Count >= _total) return;
        var q = new ItemQuery
        {
            ParentId = _view.Id,
            IncludeItemTypes = IncludeTypes,
            Recursive = IncludeTypes != null,
            SortBy = Sort.SortBy,
            SortOrder = Sort.Order,
            Filters = Filter.Filters,
            StartIndex = _cards.Count,
            Limit = PageSize,
        };
        var res = await AppServices.Jellyfin.GetItemsAsync(q, ct);
        if (ct.IsCancellationRequested) return;
        _total = res.TotalRecordCount;
        var shape = IsLandscape ? CardShape.Landscape : CardShape.Poster;
        int start = _cards.Count;
        _cards.AddRange(res.Items.Select(i => new MediaCardViewModel(i, shape)));
        AppendRows(start);
        CountText = _total == 1 ? "1 item" : $"{_total:N0} items";
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

    [RelayCommand]
    private void PlayAll()
    {
        var first = _cards.FirstOrDefault(c => c.Item.IsPlayable) ?? _cards.FirstOrDefault();
        if (first != null) AppServices.Main.Shell?.OpenItem(first.Item);
    }
}
