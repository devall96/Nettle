using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed partial class SeasonTabViewModel(DetailsViewModel owner, BaseItemDto season) : ObservableObject
{
    public BaseItemDto Season => season;
    public string Name => season.Name ?? $"Season {season.IndexNumber}";
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private Task Select() => owner.SelectSeasonAsync(this);
}

public sealed partial class EpisodeViewModel : ObservableObject
{
    public BaseItemDto Item { get; }
    public string Title { get; }
    public string? Overview => Item.Overview;
    public string Meta { get; }
    public string? ThumbUrl { get; }
    public double Progress { get; }
    public bool HasProgress => Progress > 0.005 && Progress < 0.995;
    [ObservableProperty] private bool _isPlayed;

    public EpisodeViewModel(BaseItemDto e)
    {
        Item = e;
        Title = e.IndexNumber != null ? $"{e.IndexNumber}. {e.Name}" : e.Name ?? "";
        var parts = new List<string>();
        if (e.RunTimeTicks != null) parts.Add(Format.Runtime(e.RunTimeTicks));
        if (e.PremiereDate != null) parts.Add(e.PremiereDate.Value.ToString("MMM d, yyyy"));
        Meta = string.Join("  •  ", parts);
        if (e.Tag("Primary") is { } t) ThumbUrl = AppServices.Jellyfin.ImageUrl(e.Id, "Primary", t, 480);
        Progress = (e.UserData?.PlayedPercentage ?? 0) / 100.0;
        IsPlayed = e.UserData?.Played ?? false;
    }

    [RelayCommand] private void Play() => AppServices.Main.PlayItem(Item, resume: true);
    [RelayCommand] private void Open() => AppServices.Main.Shell?.Navigate(new DetailsViewModel(Item.Id));

    [RelayCommand]
    private async Task TogglePlayed()
    {
        IsPlayed = !IsPlayed;
        try { await AppServices.Jellyfin.SetPlayedAsync(Item.Id, IsPlayed); } catch { IsPlayed = !IsPlayed; }
    }
}

public sealed class PersonViewModel(BaseItemPerson p)
{
    public string Name => p.Name ?? "";
    public string Role => p.Role ?? p.Type ?? "";
    public string Initial => string.IsNullOrEmpty(p.Name) ? "?" : p.Name[..1];
    public string? ImageUrl => p.PrimaryImageTag != null ? AppServices.Jellyfin.ImageUrl(p.Id, "Primary", p.PrimaryImageTag, 200) : null;
}

public sealed record MediaVersion(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class DetailsViewModel : PageViewModel
{
    private readonly string _itemId;
    private readonly string? _preferSeasonId;

    [ObservableProperty] private BaseItemDto? _item;
    [ObservableProperty] private string? _backdropUrl;
    [ObservableProperty] private string? _logoUrl;
    [ObservableProperty] private string? _posterUrl;
    [ObservableProperty] private string _titleText = "";
    [ObservableProperty] private string? _subtitleText;
    [ObservableProperty] private string _meta = "";
    [ObservableProperty] private string? _tagline;
    [ObservableProperty] private string? _overview;
    [ObservableProperty] private string? _genres;
    [ObservableProperty] private string? _mediaInfo;
    [ObservableProperty] private string? _hdrBadge;
    [ObservableProperty] private string? _hdrWarning;
    [ObservableProperty] private string? _endsAt;
    [ObservableProperty] private string _playLabel = "Play";
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isPlayed;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isSeries;
    [ObservableProperty] private bool _isEpisode;
    [ObservableProperty] private SeasonTabViewModel? _selectedSeason;
    [ObservableProperty] private MediaVersion? _selectedVersion;

    public ObservableCollection<SeasonTabViewModel> Seasons { get; } = new();
    public ObservableCollection<EpisodeViewModel> Episodes { get; } = new();
    public ObservableCollection<PersonViewModel> Cast { get; } = new();
    public ObservableCollection<MediaCardViewModel> Similar { get; } = new();
    public ObservableCollection<MediaVersion> Versions { get; } = new();

    public bool HasLogo => LogoUrl != null;
    public bool HasCast => Cast.Count > 0;
    public bool HasSimilar => Similar.Count > 0;
    public bool HasVersions => Versions.Count > 1;
    public bool HasProgress => Progress > 0.005 && Progress < 0.995;
    public override string Title => TitleText;

    private BaseItemDto? _playTarget;

    public DetailsViewModel(string itemId, string? preferSeasonId = null)
    {
        _itemId = itemId;
        _preferSeasonId = preferSeasonId;
    }

    partial void OnLogoUrlChanged(string? value) => OnPropertyChanged(nameof(HasLogo));
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(HasProgress));

    protected override Task LoadAsync() => RunAsync(() => LoadCoreAsync(refresh: false));
    // Coming back (e.g. from the player): update watched state and progress, but keep the
    // season the user picked, and leave cast / similar alone so nothing flickers or jumps.
    protected override Task RefreshAsync() => RunAsync(() => LoadCoreAsync(refresh: true), showBusy: false);

    private async Task LoadCoreAsync(bool refresh)
    {
        var jf = AppServices.Jellyfin;
        var item = await jf.GetItemAsync(_itemId);
        var similarTask = item.IsEpisode || (refresh && Similar.Count > 0) ? null : jf.GetSimilarAsync(item.Id, 16);
        Item = item;
        IsSeries = item.IsSeries;
        IsEpisode = item.IsEpisode;

        // Images: episodes borrow series art for the backdrop and logo.
        string? bdId = item.Id; string? bdTag = item.BackdropImageTags?.FirstOrDefault();
        if (bdTag == null && item.ParentBackdropItemId != null) { bdId = item.ParentBackdropItemId; bdTag = item.ParentBackdropImageTags?.FirstOrDefault(); }
        BackdropUrl = bdTag != null ? jf.ImageUrl(bdId, "Backdrop", bdTag, 1920, 85) : null;
        LogoUrl = item.Tag("Logo") is { } logo ? jf.ImageUrl(item.Id, "Logo", logo, 700)
            : item.ParentLogoImageTag != null ? jf.ImageUrl(item.ParentLogoItemId, "Logo", item.ParentLogoImageTag, 700) : null;
        PosterUrl = item.IsEpisode
            ? (item.Tag("Primary") is { } ep ? jf.ImageUrl(item.Id, "Primary", ep, 640) : null)
            : (item.Tag("Primary") is { } p ? jf.ImageUrl(item.Id, "Primary", p, 400) : null);

        TitleText = item.IsEpisode ? item.SeriesName ?? item.Name ?? "" : item.Name ?? "";
        SubtitleText = item.IsEpisode ? $"S{item.ParentIndexNumber} • E{item.IndexNumber}  {item.Name}" : (item.OriginalTitle != item.Name ? item.OriginalTitle : null);
        Tagline = item.Taglines?.FirstOrDefault();
        Overview = item.Overview;
        Genres = item.Genres is { Count: > 0 } ? string.Join("  ·  ", item.Genres) : null;

        var meta = new List<string>();
        if (item.ProductionYear != null)
            meta.Add(item.IsSeries && item.EndDate != null && item.EndDate.Value.Year != item.ProductionYear ? $"{item.ProductionYear}–{item.EndDate.Value.Year}" : item.ProductionYear.ToString()!);
        if (item.OfficialRating != null) meta.Add(item.OfficialRating);
        if (item.CommunityRating != null) meta.Add($"★ {item.CommunityRating:0.0}");
        if (item.CriticRating != null) meta.Add($"Critics {item.CriticRating}%");
        if (!item.IsSeries && item.RunTimeTicks != null) meta.Add(Format.Runtime(item.RunTimeTicks));
        if (item.IsSeries && item.Status == "Continuing") meta.Add("Continuing");
        Meta = string.Join("    ", meta);

        IsFavorite = item.UserData?.IsFavorite ?? false;
        IsPlayed = item.UserData?.Played ?? false;
        Progress = (item.UserData?.PlayedPercentage ?? 0) / 100.0;
        EndsAt = item.IsSeries ? null : Format.EndsAt(item.RunTimeTicks, item.UserData?.PlaybackPositionTicks ?? 0);

        BuildMediaInfo(item);

        if (!refresh)
        {
            Cast.Clear();
            foreach (var person in (item.People ?? new()).Where(x => x.Type is "Actor" or "Director" or "GuestStar").Take(24)) Cast.Add(new PersonViewModel(person));
            OnPropertyChanged(nameof(HasCast));
        }

        if (item.IsSeries) await LoadSeriesAsync(item, refresh);
        else
        {
            _playTarget = item;
            var pos = item.UserData?.PlaybackPositionTicks ?? 0;
            PlayLabel = pos > 0 ? "Resume " + Format.Clock(TimeSpan.FromTicks(pos)) : "Play";
        }

        if (similarTask != null)
        {
            try
            {
                var sim = await similarTask;
                Similar.Clear();
                foreach (var s in sim.Items) Similar.Add(new MediaCardViewModel(s, CardShape.Poster));
            }
            catch { }
            OnPropertyChanged(nameof(HasSimilar));
        }
    }

    private void BuildMediaInfo(BaseItemDto item)
    {
        var keep = SelectedVersion?.Id;
        _building = true;
        Versions.Clear();
        var sources = item.MediaSources ?? new();
        foreach (var s in sources) if (s.Id != null) Versions.Add(new MediaVersion(s.Id, s.Name ?? s.Container ?? "Version"));
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == keep) ?? Versions.FirstOrDefault();
        _building = false;
        OnPropertyChanged(nameof(HasVersions));
        ShowMediaInfo(item);
    }

    private bool _building;

    // Picking another version (e.g. 4K vs 1080p) updates the format line and HDR badge to match it.
    partial void OnSelectedVersionChanged(MediaVersion? value)
    {
        if (!_building && Item != null) ShowMediaInfo(Item);
    }

    private void ShowMediaInfo(BaseItemDto item)
    {
        var sources = item.MediaSources ?? new();
        var src = sources.FirstOrDefault(s => s.Id == SelectedVersion?.Id) ?? sources.FirstOrDefault();
        var streams = src?.MediaStreams ?? item.MediaStreams ?? new();
        var v = streams.FirstOrDefault(s => s.Type == "Video");
        var a = streams.FirstOrDefault(s => s.Type == "Audio" && s.IsDefault) ?? streams.FirstOrDefault(s => s.Type == "Audio");
        var parts = new List<string>();
        if (v != null)
        {
            var res = VideoFormat.Resolution(v.Width);
            if (res != null) parts.Add(res);
            if (v.Codec != null) parts.Add(v.Codec.ToUpperInvariant());
            HdrBadge = VideoFormat.HdrLabel(v.VideoRangeType, out var hdrWarning);
            HdrWarning = hdrWarning;
        }
        else
        {
            HdrBadge = null;
            HdrWarning = null;
        }
        if (a != null)
        {
            parts.Add(a.Codec?.ToUpperInvariant() ?? "AUDIO");
            if (a.ChannelLayout != null) parts.Add(a.ChannelLayout);
        }
        int subs = streams.Count(s => s.Type == "Subtitle");
        if (subs > 0) parts.Add(subs == 1 ? "1 subtitle" : $"{subs} subtitles");
        MediaInfo = parts.Count > 0 ? string.Join("  ·  ", parts) + "  ·  Direct Play" : null;
    }

    private async Task LoadSeriesAsync(BaseItemDto series, bool refresh)
    {
        var jf = AppServices.Jellyfin;
        var seasonsT = jf.GetSeasonsAsync(series.Id);
        var nextT = jf.GetNextUpAsync(1, series.Id);
        var seasons = await seasonsT;
        var keepSeasonId = refresh ? SelectedSeason?.Season.Id : null;
        if (!Seasons.Select(s => s.Season.Id).SequenceEqual(seasons.Items.Select(s => s.Id)))
        {
            Seasons.Clear();
            foreach (var s in seasons.Items) Seasons.Add(new SeasonTabViewModel(this, s));
        }

        BaseItemDto? next = null;
        try { next = (await nextT).Items.FirstOrDefault(); } catch { }

        var initial = Seasons.FirstOrDefault(s => keepSeasonId != null && s.Season.Id == keepSeasonId)
                      ?? Seasons.FirstOrDefault(s => s.Season.Id == _preferSeasonId)
                      ?? Seasons.FirstOrDefault(s => next != null && s.Season.Id == next.SeasonId)
                      ?? Seasons.FirstOrDefault(s => s.Season.IndexNumber > 0)
                      ?? Seasons.FirstOrDefault();
        if (initial != null) await SelectSeasonAsync(initial);

        _playTarget = next ?? Episodes.FirstOrDefault()?.Item;
        if (_playTarget != null)
        {
            var pos = _playTarget.UserData?.PlaybackPositionTicks ?? 0;
            var label = $"S{_playTarget.ParentIndexNumber} E{_playTarget.IndexNumber}";
            PlayLabel = pos > 0 ? $"Resume {label}" : next != null ? $"Play {label}" : "Play";
        }
    }

    public async Task SelectSeasonAsync(SeasonTabViewModel tab)
    {
        foreach (var s in Seasons) s.IsSelected = s == tab;
        SelectedSeason = tab;
        if (Item == null) return;
        // Only the latest pick may fill the list: quick clicks across tabs can answer out of order.
        var version = ++_seasonLoad;
        try
        {
            var eps = await AppServices.Jellyfin.GetEpisodesAsync(Item.Id, tab.Season.Id);
            if (version != _seasonLoad) return;
            if (Episodes.Select(e => e.Item.Id).SequenceEqual(eps.Items.Select(e => e.Id)))
            {
                // Same episodes (a refresh): swap each row in place so the list keeps its height
                // and the page keeps its scroll position.
                for (int i = 0; i < eps.Items.Count; i++) Episodes[i] = new EpisodeViewModel(eps.Items[i]);
            }
            else
            {
                Episodes.Clear();
                foreach (var e in eps.Items) Episodes.Add(new EpisodeViewModel(e));
            }
        }
        catch (Exception ex) { if (version == _seasonLoad) Error = ex.Message; }
    }

    private int _seasonLoad;

    [RelayCommand]
    private void Play()
    {
        if (_playTarget == null) return;
        AppServices.Main.PlayItem(_playTarget, resume: true, mediaSourceId: IsSeries ? null : SelectedVersion?.Id);
    }

    [RelayCommand]
    private void PlayFromStart()
    {
        if (_playTarget == null) return;
        AppServices.Main.PlayItem(_playTarget, resume: false, mediaSourceId: IsSeries ? null : SelectedVersion?.Id);
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        if (Item == null) return;
        IsFavorite = !IsFavorite;
        try { await AppServices.Jellyfin.SetFavoriteAsync(Item.Id, IsFavorite); } catch { IsFavorite = !IsFavorite; }
    }

    [RelayCommand]
    private async Task TogglePlayed()
    {
        if (Item == null) return;
        IsPlayed = !IsPlayed;
        try
        {
            await AppServices.Jellyfin.SetPlayedAsync(Item.Id, IsPlayed);
            if (IsSeries && SelectedSeason != null) await SelectSeasonAsync(SelectedSeason);
        }
        catch { IsPlayed = !IsPlayed; }
    }

    [RelayCommand]
    private void GoToSeries()
    {
        if (Item?.SeriesId != null) AppServices.Main.Shell?.Navigate(new DetailsViewModel(Item.SeriesId, Item.SeasonId));
    }

    [RelayCommand] private void Back() => AppServices.Main.Shell?.GoBack();
}
