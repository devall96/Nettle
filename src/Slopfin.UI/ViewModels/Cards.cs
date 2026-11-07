using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Models;

namespace Slopfin.UI.ViewModels;

public enum CardShape { Poster, Landscape, Square }

/// <summary>
/// Pre-computed presentation for one item tile. Everything the template needs is resolved once
/// here so scrolling never does string/URL work.
/// </summary>
public sealed partial class MediaCardViewModel : ObservableObject
{
    public BaseItemDto Item { get; }
    public CardShape Shape { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public string? ImageUrl { get; }
    public string? FallbackText { get; }
    public string TooltipText => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}\n{Subtitle}";
    public double Progress { get; }
    public bool HasProgress => Progress > 0.005 && Progress < 0.995;
    [ObservableProperty] private bool _isPlayed;
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    public double CardWidth => Shape switch { CardShape.Landscape => 300, CardShape.Square => 180, _ => 164 };
    public double ImageHeight => Shape switch { CardShape.Landscape => 169, CardShape.Square => 180, _ => 246 };
    public int DecodeWidth => (int)(CardWidth * 1.6);

    public MediaCardViewModel(BaseItemDto item, CardShape shape, bool preferSeriesTitle = false)
    {
        Item = item;
        Shape = shape;
        var jf = AppServices.Jellyfin;
        int w = (int)(CardWidth * 1.6);

        if (item.IsEpisode)
        {
            Title = preferSeriesTitle ? item.SeriesName ?? item.Name ?? "" : item.Name ?? "";
            var ep = item.ParentIndexNumber != null && item.IndexNumber != null ? $"S{item.ParentIndexNumber} • E{item.IndexNumber}" : null;
            Subtitle = preferSeriesTitle ? (ep != null ? $"{ep}  {item.Name}" : item.Name) : ep;
        }
        else
        {
            Title = item.Name ?? "";
            Subtitle = item.ProductionYear?.ToString() ?? (item.Type == "CollectionFolder" ? null : item.Type == "Series" ? "Series" : null);
        }
        FallbackText = Title;

        ImageUrl = shape switch
        {
            CardShape.Landscape => LandscapeImage(item, w),
            _ => PosterImage(item, w),
        };

        var ud = item.UserData;
        if (ud != null)
        {
            Progress = (ud.PlayedPercentage ?? 0) / 100.0;
            IsPlayed = ud.Played;
            if (item.IsSeries && ud.UnplayedItemCount is > 0) Badge = ud.UnplayedItemCount.ToString();
        }

        static string? PosterImage(BaseItemDto i, int w)
        {
            var jf = AppServices.Jellyfin;
            if (i.IsEpisode && i.SeriesPrimaryImageTag != null) return jf.ImageUrl(i.SeriesId, "Primary", i.SeriesPrimaryImageTag, w);
            if (i.Tag("Primary") is { } p) return jf.ImageUrl(i.Id, "Primary", p, w);
            return null;
        }

        static string? LandscapeImage(BaseItemDto i, int w)
        {
            var jf = AppServices.Jellyfin;
            if (i.IsEpisode && i.Tag("Primary") is { } ep) return jf.ImageUrl(i.Id, "Primary", ep, w);
            if (i.Tag("Thumb") is { } t) return jf.ImageUrl(i.Id, "Thumb", t, w);
            if (i.ParentThumbItemId != null && i.ParentThumbImageTag != null) return jf.ImageUrl(i.ParentThumbItemId, "Thumb", i.ParentThumbImageTag, w);
            if (i.BackdropImageTags is { Count: > 0 } b) return jf.ImageUrl(i.Id, "Backdrop", b[0], w);
            if (i.ParentBackdropItemId != null && i.ParentBackdropImageTags is { Count: > 0 } pb) return jf.ImageUrl(i.ParentBackdropItemId, "Backdrop", pb[0], w);
            if (i.Tag("Primary") is { } p) return jf.ImageUrl(i.Id, "Primary", p, w);
            return null;
        }
        _ = jf;
    }

    [RelayCommand]
    private void Open() => AppServices.Main.Shell?.OpenItem(Item);

    [RelayCommand]
    private void Play() => AppServices.Main.PlayItem(Item, resume: true);
}

public sealed class CardRowViewModel
{
    public string Title { get; }
    public IReadOnlyList<MediaCardViewModel> Items { get; }
    public CardRowViewModel(string title, IReadOnlyList<MediaCardViewModel> items)
    {
        Title = title;
        Items = items;
    }
}

/// <summary>A row of the virtualized library grid.</summary>
public sealed class GridRowViewModel
{
    public IReadOnlyList<MediaCardViewModel> Cards { get; }
    public GridRowViewModel(IReadOnlyList<MediaCardViewModel> cards) => Cards = cards;
}

public static class Format
{
    public static string Runtime(long? ticks)
    {
        if (ticks is not > 0) return "";
        var t = TimeSpan.FromTicks(ticks.Value);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{Math.Max(1, t.Minutes)}m";
    }

    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    public static string EndsAt(long? ticks, long positionTicks = 0)
    {
        if (ticks is not > 0) return "";
        var end = DateTime.Now + TimeSpan.FromTicks(ticks.Value - positionTicks);
        return "Ends at " + end.ToString("h:mm tt");
    }
}
