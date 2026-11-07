using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string IconKey { get; }
    private readonly Action _go;
    [ObservableProperty] private bool _isSelected;

    public NavItemViewModel(string key, string label, string iconKey, Action go)
    {
        Key = key;
        Label = label;
        IconKey = iconKey;
        _go = go;
    }

    [RelayCommand] private void Go() => _go();
}

/// <summary>The signed-in experience: navigation rail + page stack.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly Stack<PageViewModel> _back = new();

    [ObservableProperty] private PageViewModel? _current;
    [ObservableProperty] private bool _isAccountMenuOpen;
    public ObservableCollection<NavItemViewModel> NavTop { get; } = new();
    public ObservableCollection<NavItemViewModel> NavLibraries { get; } = new();
    public ObservableCollection<NavItemViewModel> NavBottom { get; } = new();

    public string UserName => AppServices.Session.Current?.UserName ?? "";
    public string UserInitial => string.IsNullOrEmpty(UserName) ? "?" : UserName[..1].ToUpperInvariant();
    public string ServerName => AppServices.Session.Current?.ServerName ?? "";
    public string? AvatarUrl => AppServices.Jellyfin.UserImageUrl(AppServices.Session.Current?.UserId, AppServices.Session.Current?.AvatarTag, 120);
    public bool CanGoBack => _back.Count > 0;
    public string VersionText => $"build {AppServices.Version}";

    private readonly HomeViewModel _home = new();

    public ShellViewModel(MainViewModel main)
    {
        _main = main;
        NavTop.Add(new NavItemViewModel("home", "Home", "Icon.Home", () => NavigateRoot(_home)));
        NavTop.Add(new NavItemViewModel("search", "Search", "Icon.Search", () => NavigateRoot(new SearchViewModel())));
        NavBottom.Add(new NavItemViewModel("livetv", "Live TV", "Icon.Tv", () => NavigateRoot(new LiveTvViewModel())));
        NavBottom.Add(new NavItemViewModel("requests", "Requests", "Icon.Request", () => NavigateRoot(new RequestsViewModel())));
        NavBottom.Add(new NavItemViewModel("settings", "Settings", "Icon.Settings", () => NavigateRoot(new SettingsViewModel())));
    }

    public async Task InitializeAsync()
    {
        // The last visit's libraries and home rows appear at once; fresh ones replace them.
        var cached = Services.HomeCache.Load(AppServices.Session.Current?.Id);
        if (cached is { Views.Count: > 0 })
        {
            SetNavLibraries(cached.Views);
            _home.ShowCached(cached);
        }
        NavigateRoot(_home);
        try
        {
            var views = await AppServices.Jellyfin.GetViewsAsync();
            SetNavLibraries(views.Items);
            _home.SetLibraries(views.Items);
        }
        catch { /* Home shows its own error state (or keeps showing the cached rows) */ }
    }

    private void SetNavLibraries(List<BaseItemDto> views)
    {
        var shown = views.Where(v => v.CollectionType is not ("livetv" or "playlists")).ToList();
        if (NavLibraries.Select(n => n.Key).SequenceEqual(shown.Select(v => "lib:" + v.Id))) return;
        NavLibraries.Clear();
        foreach (var v in shown)
        {
            var view = v;
            var icon = v.CollectionType switch
            {
                "movies" => "Icon.Movie",
                "tvshows" => "Icon.Tv",
                "music" => "Icon.Music",
                _ => "Icon.Library"
            };
            NavLibraries.Add(new NavItemViewModel("lib:" + v.Id, v.Name ?? "Library", icon, () => NavigateRoot(new LibraryViewModel(view))));
        }
        var key = Current?.NavKey ?? "";
        foreach (var n in NavLibraries) n.IsSelected = n.Key == key;
    }

    /// <summary>Name / picture may have changed on the server since the account was saved.</summary>
    public void RefreshAccount()
    {
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserInitial));
        OnPropertyChanged(nameof(AvatarUrl));
        OnPropertyChanged(nameof(ServerName));
    }

    public void GoHome() => NavigateRoot(_home);

    /// <summary>Everything in the libraries with this person in it (actor, director…).</summary>
    public void OpenPerson(string personId, string name) => Navigate(LibraryViewModel.ForPerson(personId, name));

    partial void OnCurrentChanged(PageViewModel? oldValue, PageViewModel? newValue)
    {
        oldValue?.OnDeactivated();
        var key = newValue?.NavKey ?? "";
        foreach (var n in NavTop.Concat(NavLibraries).Concat(NavBottom)) n.IsSelected = n.Key == key;
        OnPropertyChanged(nameof(CanGoBack));
        if (newValue != null) _ = newValue.ActivateAsync();
    }

    /// <summary>Top-level destination from the rail: resets the back stack.</summary>
    public void NavigateRoot(PageViewModel page)
    {
        if (Current?.GetType() == page.GetType() && page is not LibraryViewModel && page is not HomeViewModel) return;
        if (Current is LibraryViewModel cur && page is LibraryViewModel next && cur.ViewId == next.ViewId) return;
        _back.Clear();
        Current = page;
    }

    public void Navigate(PageViewModel page)
    {
        if (Current != null) _back.Push(Current);
        Current = page;
    }

    [RelayCommand]
    public void GoBack()
    {
        IsAccountMenuOpen = false;
        if (Current?.HandleBack() == true) return;
        if (_back.Count > 0) Current = _back.Pop();
        else if (Current != _home) Current = _home;
    }

    public void OpenItem(BaseItemDto item)
    {
        switch (item.Type)
        {
            case "CollectionFolder":
            case "UserView":
                Navigate(new LibraryViewModel(item));
                break;
            case "Folder":
            case "BoxSet":
            case "Playlist":
                Navigate(new LibraryViewModel(item, folderMode: true));
                break;
            case "Season":
                Navigate(new DetailsViewModel(item.SeriesId ?? item.Id, preferSeasonId: item.Id));
                break;
            default:
                Navigate(new DetailsViewModel(item.Id));
                break;
        }
    }

    public void RefreshCurrent() => _ = Current?.ActivateAsync();

    public void OpenSearchWith(string query)
    {
        var s = new SearchViewModel { Query = query };
        NavigateRoot(s);
    }

    [RelayCommand] private void ToggleAccountMenu() => IsAccountMenuOpen = !IsAccountMenuOpen;
    [RelayCommand] private void SwitchUser() { IsAccountMenuOpen = false; _main.SwitchUser(); }

    [RelayCommand]
    private async Task SignOut()
    {
        IsAccountMenuOpen = false;
        var acc = AppServices.Session.Current;
        AppServices.Remote.Stop();
        if (acc != null)
        {
            await AppServices.Session.SignOutAndForgetAsync(acc);
            Services.HomeCache.Delete(acc.Id);
        }
        _main.ShowAccountPicker();
    }
}
