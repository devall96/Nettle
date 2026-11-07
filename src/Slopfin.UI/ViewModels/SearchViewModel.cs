using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Api;

namespace Slopfin.UI.ViewModels;

public sealed partial class SearchViewModel : PageViewModel
{
    public override string NavKey => "search";
    public ObservableCollection<CardRowViewModel> Results { get; } = new();
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _noResults;
    private CancellationTokenSource? _cts;

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
            NoResults = false;
            return;
        }

        await RunAsync(async () =>
        {
            var jf = AppServices.Jellyfin;
            var res = await jf.GetItemsAsync(new ItemQuery
            {
                SearchTerm = text.Trim(),
                IncludeItemTypes = "Movie,Series,Episode,BoxSet,MusicVideo,Video",
                Recursive = true,
                SortBy = null,
                SortOrder = null,
                Limit = 60,
            }, cts.Token);
            if (cts.IsCancellationRequested) return;

            Results.Clear();
            void AddRow(string title, string type, CardShape shape)
            {
                var items = res.Items.Where(i => i.Type == type).Select(i => new MediaCardViewModel(i, shape, true)).ToList();
                if (items.Count > 0) Results.Add(new CardRowViewModel(title, items));
            }
            AddRow("Movies", "Movie", CardShape.Poster);
            AddRow("Shows", "Series", CardShape.Poster);
            AddRow("Episodes", "Episode", CardShape.Landscape);
            AddRow("Collections", "BoxSet", CardShape.Poster);
            AddRow("Videos", "Video", CardShape.Landscape);
            NoResults = Results.Count == 0;
        }, showBusy: true);
    }

    [RelayCommand]
    private void RequestIt()
    {
        var req = new RequestsViewModel { SearchText = Query };
        AppServices.Main.Shell?.NavigateRoot(req);
    }
}
