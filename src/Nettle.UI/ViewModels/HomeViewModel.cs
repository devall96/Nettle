using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;
using Nettle.UI.Services;

namespace Nettle.UI.ViewModels;

public sealed partial class HeroItemViewModel : ObservableObject
{
    public BaseItemDto Item { get; }
    public string Title => Item.Name ?? "";
    public string? Overview => Item.Overview;
    public string? BackdropUrl { get; }
    public string? LogoUrl { get; }
    public bool HasLogo => LogoUrl != null;
    public string Meta { get; }

    public HeroItemViewModel(BaseItemDto item)
    {
        Item = item;
        var jf = AppServices.Jellyfin;
        if (item.BackdropImageTags is { Count: > 0 } b) BackdropUrl = jf.ImageUrl(item.Id, "Backdrop", b[0], 1920, 85);
        if (item.Tag("Logo") is { } logo) LogoUrl = jf.ImageUrl(item.Id, "Logo", logo, 700);
        var parts = new List<string>();
        if (item.ProductionYear != null) parts.Add(item.ProductionYear.ToString()!);
        if (item.OfficialRating != null) parts.Add(item.OfficialRating);
        if (item.CommunityRating != null) parts.Add($"★ {item.CommunityRating:0.0}");
        if (item.IsSeries) parts.Add("Series");
        else if (item.RunTimeTicks != null) parts.Add(Format.Runtime(item.RunTimeTicks));
        if (item.Genres is { Count: > 0 }) parts.Add(string.Join(", ", item.Genres.Take(3)));
        Meta = string.Join("   •   ", parts);
    }

    [RelayCommand]
    private void Play()
    {
        if (Item.IsPlayable) AppServices.Main.PlayItem(Item, resume: true);
        else AppServices.Main.Shell?.OpenItem(Item);
    }
    [RelayCommand] private void Info() => AppServices.Main.Shell?.OpenItem(Item);
}

public sealed partial class HomeViewModel : PageViewModel
{
    public override string NavKey => "home";
    public ObservableCollection<CardRowViewModel> Rows { get; } = new();
    public ObservableCollection<HeroItemViewModel> Heroes { get; } = new();
    [ObservableProperty] private HeroItemViewModel? _hero;
    [ObservableProperty] private int _heroIndex;
    [ObservableProperty] private bool _isEmpty;
    private List<BaseItemDto>? _libraries;
    private readonly DispatcherTimer _heroTimer;

    public bool HasHero => Hero != null;

    public HomeViewModel()
    {
        _heroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _heroTimer.Tick += (_, _) => NextHero();
    }

    partial void OnHeroChanged(HeroItemViewModel? value) => OnPropertyChanged(nameof(HasHero));

    private bool _showingCache;

    public void SetLibraries(List<BaseItemDto> libs)
    {
        _libraries = libs;
        _ = ReloadAsync(quiet: _showingCache);
    }

    /// <summary>Paints the last visit's home screen at once (from the cache), before fresh data arrives.</summary>
    public void ShowCached(HomeSnapshot snapshot)
    {
        if (Rows.Count > 0 || snapshot.Views.Count == 0) return;
        _libraries ??= snapshot.Views;
        _showingCache = true;
        Render(snapshot, newHeroes: true);
    }

    protected override Task LoadAsync() => _libraries == null ? Task.CompletedTask : ReloadAsync(quiet: _showingCache);
    protected override Task RefreshAsync() => ReloadAsync(quiet: true);
    // Reduce motion: the banner doesn't rotate by itself (arrows still work).
    protected override void OnActivated() { if (!AppServices.Settings.Current.ReduceMotion) _heroTimer.Start(); }
    public override void OnDeactivated() => _heroTimer.Stop();

    /// <summary>Libraries that get a "Recently added" row: video ones the user hasn't hidden from it in Jellyfin.</summary>
    private static IEnumerable<BaseItemDto> LatestLibraries(IEnumerable<BaseItemDto> libs)
    {
        var hidden = AppServices.Session.LatestExcludes;
        return libs.Where(l => l.CollectionType is "movies" or "tvshows" or "homevideos" or "musicvideos" or null or "mixed" or "boxsets"
                               && !hidden.Contains(l.Id, StringComparer.OrdinalIgnoreCase));
    }

    private async Task ReloadAsync(bool quiet = false)
    {
        if (_libraries == null) return;
        var libraries = _libraries;
        await RunAsync(async () =>
        {
            var jf = AppServices.Jellyfin;
            var resumeT = jf.GetResumeAsync(16);
            var nextUpT = jf.GetNextUpAsync(16);
            var latestTasks = LatestLibraries(libraries).Select(l => (lib: l, task: jf.GetLatestAsync(l.Id, 20))).ToList();

            var snapshot = new HomeSnapshot { Views = libraries, Resume = (await resumeT).Items, NextUp = (await nextUpT).Items };
            foreach (var (lib, task) in latestTasks)
            {
                try { snapshot.Latest[lib.Id] = await task; } catch { /* one library failing doesn't hide the rest */ }
            }

            Render(snapshot, newHeroes: !quiet || Heroes.Count == 0 || _showingCache);
            _showingCache = false;
            HomeCache.Save(AppServices.Session.Current?.Id, snapshot);
        }, showBusy: !quiet && Rows.Count == 0);
    }

    private void Render(HomeSnapshot s, bool newHeroes)
    {
        var rows = new List<CardRowViewModel>();
        if (s.Resume.Count > 0)
            rows.Add(new CardRowViewModel("Continue Watching", s.Resume.Select(i => new MediaCardViewModel(i, CardShape.Landscape, true)).ToList()));
        if (s.NextUp.Count > 0)
            rows.Add(new CardRowViewModel("Next Up", s.NextUp.Select(i => new MediaCardViewModel(i, CardShape.Landscape, true)).ToList()));

        var heroPool = new List<BaseItemDto>();
        foreach (var lib in LatestLibraries(s.Views))
        {
            if (!s.Latest.TryGetValue(lib.Id, out var items) || items.Count == 0) continue;
            rows.Add(new CardRowViewModel("Recently Added in " + lib.Name, items.Select(i => new MediaCardViewModel(i, CardShape.Poster, true)).ToList()));
            heroPool.AddRange(items.Where(i => (i.IsMovie || i.IsSeries) && i.BackdropImageTags is { Count: > 0 }).Take(4));
        }

        rows.Add(new CardRowViewModel("My Libraries", s.Views.Select(l => new MediaCardViewModel(l, CardShape.Landscape)).ToList()));

        // Update in place (e.g. after closing the player), so the page and each row keep
        // their scroll position and only changed cards are redrawn.
        CardRowViewModel.SyncRows(Rows, rows);

        if (newHeroes)
        {
            Heroes.Clear();
            foreach (var h in heroPool.OrderBy(_ => Random.Shared.Next()).Take(8)) Heroes.Add(new HeroItemViewModel(h));
            HeroIndex = 0;
            Hero = Heroes.FirstOrDefault();
        }
        IsEmpty = rows.Count <= 1 && Heroes.Count == 0;
    }

    [RelayCommand]
    private void NextHero()
    {
        if (Heroes.Count == 0) return;
        HeroIndex = (HeroIndex + 1) % Heroes.Count;
        Hero = Heroes[HeroIndex];
    }

    [RelayCommand]
    private void PrevHero()
    {
        if (Heroes.Count == 0) return;
        HeroIndex = (HeroIndex - 1 + Heroes.Count) % Heroes.Count;
        Hero = Heroes[HeroIndex];
        _heroTimer.Stop();
        if (!AppServices.Settings.Current.ReduceMotion) _heroTimer.Start();
    }
}
