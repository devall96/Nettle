using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed partial class SyncGroupViewModel(WatchTogetherViewModel owner, SyncPlayGroupInfo group)
{
    public SyncPlayGroupInfo Group => group;
    public string Name => group.Name;
    public string People => group.Participants.Count == 0 ? "Nobody yet" : string.Join(", ", group.Participants);
    public string StateText => WatchTogetherViewModel.Describe(group.State);
    [RelayCommand] private Task Join() => owner.JoinAsync(this);
}

/// <summary>
/// Watch together (Jellyfin SyncPlay): start or join a group, and from then on what anyone plays, pauses or seeks
/// happens for everyone in it, on any Jellyfin app that supports SyncPlay.
/// </summary>
public sealed partial class WatchTogetherViewModel : PageViewModel
{
    public override string NavKey => "together";
    public ObservableCollection<SyncGroupViewModel> Groups { get; } = new();
    [ObservableProperty] private bool _inGroup;
    [ObservableProperty] private string? _groupName;
    [ObservableProperty] private string? _groupPeople;
    [ObservableProperty] private string? _groupState;
    [ObservableProperty] private bool _noGroups;

    private static Nettle.Core.Services.SyncPlayService Sync => AppServices.SyncPlay;

    public WatchTogetherViewModel() => UpdateGroup();

    protected override void OnActivated() { Sync.Changed -= OnSyncChanged; Sync.Changed += OnSyncChanged; UpdateGroup(); }

    private void OnSyncChanged() => Dispatcher.UIThread.Post(UpdateGroup);

    private void UpdateGroup()
    {
        InGroup = Sync.InGroup;
        GroupName = Sync.Group?.Name;
        GroupPeople = Sync.Group is { } g ? string.Join(", ", g.Participants) : null;
        GroupState = Describe(Sync.State);
    }

    internal static string Describe(string state) => state switch
    {
        "Playing" => "Playing",
        "Paused" => "Paused",
        "Waiting" => "Waiting for everyone to be ready",
        _ => "Nothing playing yet",
    };

    protected override Task LoadAsync() => RefreshAsync();

    protected override Task RefreshAsync() => RunAsync(async () =>
    {
        var list = await Sync.ListAsync();
        Groups.Clear();
        foreach (var g in list.Where(g => g.GroupId != Sync.Group?.GroupId)) Groups.Add(new SyncGroupViewModel(this, g));
        NoGroups = Groups.Count == 0;
    });

    public override void OnDeactivated() => Sync.Changed -= OnSyncChanged;

    [RelayCommand]
    private Task Create() => RunAsync(async () =>
    {
        var name = $"{AppServices.Session.Current?.UserName ?? "Nettle"}'s group";
        await Sync.CreateAsync(name);
        AppServices.Main.Toast("Group started. Anything you play now plays for everyone who joins.", 4500);
    });

    public Task JoinAsync(SyncGroupViewModel g) => RunAsync(async () =>
    {
        await Sync.JoinAsync(g.Group.GroupId);
        AppServices.Main.Toast($"Joined {g.Name}", 3000);
        await RefreshAsync();
    });

    [RelayCommand]
    private Task Leave() => RunAsync(async () =>
    {
        await Sync.LeaveAsync();
        AppServices.Main.Toast("You left the group", 2500);
        await RefreshAsync();
    });

    [RelayCommand] private Task Reload() => RefreshAsync();
}
