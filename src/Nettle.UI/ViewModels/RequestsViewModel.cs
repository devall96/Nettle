using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed partial class SeerrCardViewModel : ObservableObject
{
    public SeerrResult Result { get; }
    public string Title => Result.DisplayTitle;
    public string Subtitle => $"{(Result.MediaType == "tv" ? "Series" : "Movie")}{(Result.Year != null ? "  •  " + Result.Year : "")}";
    public string? PosterUrl => AppServices.Seerr.ImageUrl(Result.PosterPath, 342);
    [ObservableProperty] private string _status;
    [ObservableProperty] private bool _canRequest;
    [ObservableProperty] private bool _isRequesting;
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    private readonly RequestsViewModel _owner;

    public SeerrCardViewModel(SeerrResult r, RequestsViewModel owner)
    {
        Result = r;
        _owner = owner;
        _status = SeerrMediaStatus.Describe(r.MediaInfo?.Status);
        _canRequest = r.MediaInfo?.Status is null or SeerrMediaStatus.Unknown or SeerrMediaStatus.PartiallyAvailable;
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    [RelayCommand]
    private async Task Request()
    {
        if (!CanRequest || IsRequesting) return;
        // Series: choose seasons first.
        if (Result.MediaType == "tv") { await _owner.OpenSeasonPickerAsync(this); return; }
        await SubmitAsync(null);
    }

    public async Task SubmitAsync(IReadOnlyCollection<int>? seasons)
    {
        IsRequesting = true;
        try
        {
            await AppServices.Seerr.RequestAsync(Result, seasons);
            Status = "Requested";
            CanRequest = false;
            AppServices.Main.Toast(seasons is { Count: > 0 }
                ? $"Requested “{Title}” ({(seasons.Count == 1 ? "season " + seasons.First() : seasons.Count + " seasons")})"
                : $"Requested “{Title}”");
            _ = _owner.ReloadMyRequestsAsync();
        }
        catch (Exception ex)
        {
            AppServices.Main.Toast("Request failed: " + ex.Message, 4000);
        }
        finally { IsRequesting = false; }
    }
}

public sealed partial class SeasonOptionViewModel : ObservableObject
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>False when the season is already available or requested.</summary>
    public bool CanSelect { get; init; } = true;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Season picker shown before requesting a series.</summary>
public sealed partial class SeasonPickerViewModel : ObservableObject
{
    private readonly SeerrCardViewModel _card;
    private readonly Action _close;
    public string Title => _card.Title;
    public List<SeasonOptionViewModel> Seasons { get; }
    public bool HasSelectable => Seasons.Any(s => s.CanSelect);

    public SeasonPickerViewModel(SeerrCardViewModel card, List<SeasonOptionViewModel> seasons, Action close)
    {
        _card = card;
        _close = close;
        Seasons = seasons;
        foreach (var s in seasons) s.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(RequestText));
            RequestCommand.NotifyCanExecuteChanged();
        };
    }

    public int SelectedCount => Seasons.Count(s => s.IsSelected);
    public string RequestText => SelectedCount switch { 0 => "Choose seasons", 1 => "Request 1 season", var n => $"Request {n} seasons" };

    [RelayCommand]
    private void SelectAll()
    {
        bool all = Seasons.Where(s => s.CanSelect).All(s => s.IsSelected);
        foreach (var s in Seasons.Where(s => s.CanSelect)) s.IsSelected = !all;
    }

    private bool CanRequest() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private async Task Request()
    {
        var picked = Seasons.Where(s => s.IsSelected).Select(s => s.Number).ToList();
        _close();
        await _card.SubmitAsync(picked);
    }

    [RelayCommand] private void Cancel() => _close();
}

public sealed class SeerrRowViewModel(string title, IReadOnlyList<SeerrCardViewModel> items)
{
    public string Title => title;
    public IReadOnlyList<SeerrCardViewModel> Items => items;
}

public sealed class MyRequestViewModel
{
    public string Title { get; init; } = "";
    public string? PosterUrl { get; init; }
    public string Status { get; init; } = "";
    public string When { get; init; } = "";
}

public sealed partial class RequestsViewModel : PageViewModel
{
    public override string NavKey => "requests";
    public ObservableCollection<SeerrRowViewModel> Rows { get; } = new();
    public ObservableCollection<SeerrCardViewModel> SearchResults { get; } = new();
    public ObservableCollection<MyRequestViewModel> MyRequests { get; } = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _notConfigured;
    [ObservableProperty] private SeasonPickerViewModel? _seasonPicker;
    [ObservableProperty] private bool _isLoadingSeasons;
    private CancellationTokenSource? _cts;

    /// <summary>Opens the season picker for a series (seasons already in the library or requested can't be picked).</summary>
    public async Task OpenSeasonPickerAsync(SeerrCardViewModel card)
    {
        IsLoadingSeasons = true;
        try
        {
            var tv = await AppServices.Seerr.TvAsync(card.Result.Id);
            var status = tv.MediaInfo?.Seasons?.ToDictionary(s => s.SeasonNumber, s => s.Status) ?? new();
            var options = (tv.Seasons ?? new())
                .Where(s => s.SeasonNumber > 0)
                .OrderBy(s => s.SeasonNumber)
                .Select(s =>
                {
                    var st = status.TryGetValue(s.SeasonNumber, out var v) ? v : SeerrMediaStatus.Unknown;
                    bool can = st is SeerrMediaStatus.Unknown or < SeerrMediaStatus.Pending;
                    var detail = s.EpisodeCount > 0 ? $"{s.EpisodeCount} episodes" : "";
                    if (!can) detail = SeerrMediaStatus.Describe(st) + (detail.Length > 0 ? " · " + detail : "");
                    return new SeasonOptionViewModel
                    {
                        Number = s.SeasonNumber,
                        Name = string.IsNullOrWhiteSpace(s.Name) ? $"Season {s.SeasonNumber}" : s.Name!,
                        Detail = detail,
                        CanSelect = can,
                    };
                })
                .ToList();
            if (options.Count == 0) { await card.SubmitAsync(null); return; }
            SeasonPicker = new SeasonPickerViewModel(card, options, () => SeasonPicker = null);
        }
        catch (Exception ex) { AppServices.Main.Toast("Couldn't load seasons: " + ex.Message, 4000); }
        finally { IsLoadingSeasons = false; }
    }

    public Task ReloadMyRequestsAsync() => LoadMyRequestsAsync();

    public override bool HandleBack()
    {
        if (SeasonPicker == null) return false;
        SeasonPicker = null;
        return true;
    }

    public bool HasMyRequests => MyRequests.Count > 0;

    partial void OnSearchTextChanged(string value) => _ = SearchAsync(value);

    protected override async Task LoadAsync()
    {
        NotConfigured = !AppServices.Seerr.IsConfigured;
        if (NotConfigured) return;
        if (!string.IsNullOrWhiteSpace(SearchText)) _ = SearchAsync(SearchText, 0);
        await RunAsync(async () =>
        {
            var s = AppServices.Seerr;
            var trending = s.TrendingAsync();
            var movies = s.PopularMoviesAsync();
            var tv = s.PopularTvAsync();
            var upcoming = s.UpcomingMoviesAsync();
            Rows.Clear();
            Add("Trending", await trending);
            Add("Popular Movies", await movies);
            Add("Popular Series", await tv);
            Add("Coming Soon", await upcoming);
            await LoadMyRequestsAsync();
        });

        void Add(string title, SeerrPage page)
        {
            var items = page.Results.Where(r => r.MediaType is "movie" or "tv" or null).Select(r => new SeerrCardViewModel(r, this)).ToList();
            if (items.Count > 0) Rows.Add(new SeerrRowViewModel(title, items));
        }
    }

    private async Task LoadMyRequestsAsync()
    {
        MyRequests.Clear();
        try
        {
            var mine = await AppServices.Seerr.MyRequestsAsync(20);
            var tasks = mine.Where(r => r.Media?.TmdbId != null).Take(20).Select(async r =>
            {
                SeerrDetails? d = null;
                try
                {
                    d = r.Media!.MediaType == "tv" ? await AppServices.Seerr.TvAsync(r.Media.TmdbId!.Value) : await AppServices.Seerr.MovieAsync(r.Media.TmdbId!.Value);
                }
                catch { }
                var status = r.Status switch { 1 => "Pending approval", 3 => "Declined", 4 => "Failed", _ => SeerrMediaStatus.Describe(r.Media?.Status) };
                return new MyRequestViewModel
                {
                    Title = d?.Title ?? d?.Name ?? "Unknown title",
                    PosterUrl = AppServices.Seerr.ImageUrl(d?.PosterPath, 185),
                    Status = string.IsNullOrEmpty(status) ? "Approved" : status,
                    When = r.CreatedAt?.LocalDateTime.ToString("MMM d") ?? "",
                };
            }).ToList();
            foreach (var t in tasks) MyRequests.Add(await t);
        }
        catch { }
        OnPropertyChanged(nameof(HasMyRequests));
    }

    private async Task SearchAsync(string text, int delay = 350)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { await Task.Delay(delay, cts.Token); } catch (TaskCanceledException) { return; }
        IsSearching = !string.IsNullOrWhiteSpace(text);
        SearchResults.Clear();
        if (!IsSearching || NotConfigured) return;
        await RunAsync(async () =>
        {
            var page = await AppServices.Seerr.SearchAsync(text.Trim(), 1, cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var r in page.Results.Where(r => r.MediaType is "movie" or "tv")) SearchResults.Add(new SeerrCardViewModel(r, this));
        });
    }

    [RelayCommand] private void OpenSettings() => AppServices.Main.Shell?.NavigateRoot(new SettingsViewModel());
    [RelayCommand] private void ClearSearch() => SearchText = "";
}
