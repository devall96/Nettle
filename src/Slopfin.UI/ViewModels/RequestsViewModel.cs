using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Api;
using Slopfin.Core.Models;

namespace Slopfin.UI.ViewModels;

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

    public SeerrCardViewModel(SeerrResult r)
    {
        Result = r;
        _status = SeerrMediaStatus.Describe(r.MediaInfo?.Status);
        _canRequest = r.MediaInfo?.Status is null or SeerrMediaStatus.Unknown or SeerrMediaStatus.PartiallyAvailable;
    }

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    [RelayCommand]
    private async Task Request()
    {
        if (!CanRequest || IsRequesting) return;
        IsRequesting = true;
        try
        {
            await AppServices.Seerr.RequestAsync(Result);
            Status = "Requested";
            CanRequest = false;
            AppServices.Main.Toast($"Requested “{Title}”");
        }
        catch (Exception ex)
        {
            AppServices.Main.Toast("Request failed: " + ex.Message, 4000);
        }
        finally { IsRequesting = false; }
    }
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
    private CancellationTokenSource? _cts;

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
            var items = page.Results.Where(r => r.MediaType is "movie" or "tv" or null).Select(r => new SeerrCardViewModel(r)).ToList();
            if (items.Count > 0) Rows.Add(new SeerrRowViewModel(title, items));
        }
    }

    private async Task LoadMyRequestsAsync()
    {
        MyRequests.Clear();
        try
        {
            var page = await AppServices.Seerr.MyRequestsAsync(20);
            var tasks = page.Results.Where(r => r.Media?.TmdbId != null).Take(20).Select(async r =>
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
            foreach (var r in page.Results.Where(r => r.MediaType is "movie" or "tv")) SearchResults.Add(new SeerrCardViewModel(r));
        });
    }

    [RelayCommand] private void OpenSettings() => AppServices.Main.Shell?.NavigateRoot(new SettingsViewModel());
    [RelayCommand] private void ClearSearch() => SearchText = "";
}
