using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Models;

namespace Slopfin.UI.ViewModels;

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

    public void SetLibraries(List<BaseItemDto> libs)
    {
        _libraries = libs;
        _ = ReloadAsync();
    }

    protected override Task LoadAsync() => _libraries == null ? Task.CompletedTask : ReloadAsync();
    protected override Task RefreshAsync() => ReloadAsync(quiet: true);
    protected override void OnActivated() => _heroTimer.Start();
    public override void OnDeactivated() => _heroTimer.Stop();

    private async Task ReloadAsync(bool quiet = false)
    {
        if (_libraries == null) return;
        await RunAsync(async () =>
        {
            var jf = AppServices.Jellyfin;
            var resumeT = jf.GetResumeAsync(16);
            var nextUpT = jf.GetNextUpAsync(16);
            var latestTasks = _libraries
                .Where(l => l.CollectionType is "movies" or "tvshows" or "homevideos" or "musicvideos" or null or "mixed" or "boxsets")
                .Select(l => (lib: l, task: jf.GetLatestAsync(l.Id, 20)))
                .ToList();

            var rows = new List<CardRowViewModel>();
            var resume = await resumeT;
            if (resume.Items.Count > 0)
                rows.Add(new CardRowViewModel("Continue Watching", resume.Items.Select(i => new MediaCardViewModel(i, CardShape.Landscape, true)).ToList()));
            var next = await nextUpT;
            if (next.Items.Count > 0)
                rows.Add(new CardRowViewModel("Next Up", next.Items.Select(i => new MediaCardViewModel(i, CardShape.Landscape, true)).ToList()));

            var heroPool = new List<BaseItemDto>();
            foreach (var (lib, task) in latestTasks)
            {
                List<BaseItemDto> items;
                try { items = await task; } catch { continue; }
                if (items.Count == 0) continue;
                rows.Add(new CardRowViewModel("Recently Added in " + lib.Name, items.Select(i => new MediaCardViewModel(i, CardShape.Poster, true)).ToList()));
                heroPool.AddRange(items.Where(i => (i.IsMovie || i.IsSeries) && i.BackdropImageTags is { Count: > 0 }).Take(4));
            }

            rows.Add(new CardRowViewModel("My Libraries", _libraries.Select(l => new MediaCardViewModel(l, CardShape.Landscape)).ToList()));

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);

            if (!quiet || Heroes.Count == 0)
            {
                Heroes.Clear();
                foreach (var h in heroPool.OrderBy(_ => Random.Shared.Next()).Take(8)) Heroes.Add(new HeroItemViewModel(h));
                HeroIndex = 0;
                Hero = Heroes.FirstOrDefault();
            }
            IsEmpty = rows.Count <= 1 && Heroes.Count == 0;
        }, showBusy: !quiet);
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
        _heroTimer.Start();
    }
}
