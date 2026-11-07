using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

// ===================== "Who's watching?" =====================

public sealed partial class AccountTileViewModel : ObservableObject
{
    private readonly AccountPickerViewModel _owner;
    public SavedAccount Account { get; }
    public string Name => Account.UserName;
    public string Server => Account.ServerName ?? new Uri(Account.ServerUrl).Host;
    public string Initial => string.IsNullOrEmpty(Account.UserName) ? "?" : Account.UserName[..1].ToUpperInvariant();
    public string? AvatarUrl => AppServices.Jellyfin.UserImageUrl(Account.UserId, Account.AvatarTag, 240, Account.ServerUrl);
    [ObservableProperty] private bool _isSigningIn;

    public AccountTileViewModel(AccountPickerViewModel owner, SavedAccount account)
    {
        _owner = owner;
        Account = account;
    }

    [RelayCommand] private Task Select() => _owner.SignInAsync(this);
    [RelayCommand] private Task Forget() => _owner.ForgetAsync(this);
}

public sealed partial class AccountPickerViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    public ObservableCollection<AccountTileViewModel> Accounts { get; } = new();
    [ObservableProperty] private bool _isEditing;

    public bool AutoLogin
    {
        get => AppServices.Settings.Current.AutoLogin;
        set
        {
            AppServices.Settings.Current.AutoLogin = value;
            _ = AppServices.Settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public AccountPickerViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var a in AppServices.Session.SavedAccounts) Accounts.Add(new AccountTileViewModel(this, a));
    }

    public async Task SignInAsync(AccountTileViewModel tile)
    {
        if (IsBusy) return;
        tile.IsSigningIn = true;
        bool ok = false;
        await RunAsync(async () => ok = await AppServices.Session.ResumeAsync(tile.Account));
        tile.IsSigningIn = false;
        if (ok) _main.ShowShell();
        else if (Error == null)
            _main.ShowLogin(tile.Account.ServerUrl, prefillUser: tile.Account.UserName, message: "Your session expired. Please sign in again.");
    }

    public async Task ForgetAsync(AccountTileViewModel tile)
    {
        bool wasCurrent = AppServices.Session.Current?.Id == tile.Account.Id;
        await AppServices.Session.SignOutAndForgetAsync(tile.Account);
        Accounts.Remove(tile);
        if (wasCurrent) _main.ShowAccountPicker();
        else if (Accounts.Count == 0 && AppServices.Session.Current == null) _main.ShowServerSelect();
    }

    [RelayCommand] private void AddAccount() => _main.ShowServerSelect();
    [RelayCommand] private void ToggleEdit() => IsEditing = !IsEditing;
}

// ===================== Server selection =====================

public sealed partial class DiscoveredServerViewModel(ServerSelectViewModel owner, DiscoveredServer server) : ObservableObject
{
    public string Name => server.Name ?? "Jellyfin";
    public string Address => server.Address ?? "";
    [RelayCommand] private Task Connect() => owner.ConnectToAsync(Address);
}

public sealed partial class ServerSelectViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private bool _isScanning;
    public ObservableCollection<DiscoveredServerViewModel> Discovered { get; } = new();
    public bool CanGoBack => AppServices.Session.SavedAccounts.Count > 0;

    public ServerSelectViewModel(MainViewModel main)
    {
        _main = main;
        _ = ScanAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        Discovered.Clear();
        var found = await JellyfinClient.DiscoverAsync(TimeSpan.FromSeconds(3));
        foreach (var s in found) Discovered.Add(new DiscoveredServerViewModel(this, s));
        IsScanning = false;
    }

    /// <summary>http or https for a typed address; the port is filled in automatically.</summary>
    public bool IsHttps
    {
        get => AppServices.Settings.Current.ServerScheme == "https";
        set
        {
            AppServices.Settings.Current.ServerScheme = value ? "https" : "http";
            _ = AppServices.Settings.SaveAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHttp));
            OnPropertyChanged(nameof(PortHint));
        }
    }
    public bool IsHttp => !IsHttps;
    public string PortHint => IsHttps
        ? "Port 8920 is added for you (or 443 for a name like jellyfin.example.com). Type :port to use another."
        : "Port 8096 is added for you. Type :port to use another.";

    [RelayCommand] private void UseHttp() => IsHttps = false;
    [RelayCommand] private void UseHttps() => IsHttps = true;

    [RelayCommand]
    private Task Connect() => ConnectToAsync(Address, AppServices.Settings.Current.ServerScheme);

    public async Task ConnectToAsync(string address, string? scheme = null)
    {
        if (string.IsNullOrWhiteSpace(address)) { Error = "Enter your server's IP address, e.g. 192.168.1.20"; return; }
        await RunAsync(async () =>
        {
            var (url, info) = await AppServices.Jellyfin.ProbeServerAsync(address, scheme: scheme);
            _main.ShowLogin(url, info);
        });
    }

    [RelayCommand] private void Back() => _main.ShowAccountPicker();
}

// ===================== Sign in =====================

public sealed partial class PublicUserViewModel(LoginViewModel owner, UserDto user, string serverUrl) : ObservableObject
{
    public UserDto User => user;
    public string Name => user.Name ?? "";
    public string Initial => string.IsNullOrEmpty(user.Name) ? "?" : user.Name[..1].ToUpperInvariant();
    public string? AvatarUrl => AppServices.Jellyfin.UserImageUrl(user.Id, user.PrimaryImageTag, 240, serverUrl);
    [RelayCommand] private Task Select() => owner.SelectUserAsync(this);
}

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly string _serverUrl;
    private PublicSystemInfo? _info;
    private CancellationTokenSource? _qcCts;

    public ObservableCollection<PublicUserViewModel> Users { get; } = new();
    public string ServerName => _info?.ServerName ?? new Uri(_serverUrl).Host;
    public string ServerDetail => $"{_serverUrl}{(_info?.Version != null ? "  •  Jellyfin " + _info.Version : "")}";
    public bool IsInsecureConnection => NetworkSafety.IsInsecureRemote(_serverUrl);

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _rememberMe = true;
    [ObservableProperty] private bool _showManualForm;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _quickConnectAvailable;
    [ObservableProperty] private string? _quickConnectCode;
    [ObservableProperty] private bool _passwordFocusRequested;

    public bool HasUsers => Users.Count > 0;
    public bool IsQuickConnectActive => QuickConnectCode != null;

    public LoginViewModel(MainViewModel main, string serverUrl, PublicSystemInfo? info, string? prefillUser, string? message)
    {
        _main = main;
        _serverUrl = serverUrl;
        _info = info;
        Message = message;
        if (prefillUser != null)
        {
            Username = prefillUser;
            ShowManualForm = true;
        }
        AppServices.Session.BeginLogin(serverUrl);
        _ = InitAsync();
    }

    partial void OnQuickConnectCodeChanged(string? value) => OnPropertyChanged(nameof(IsQuickConnectActive));

    private async Task InitAsync()
    {
        await RunAsync(async () =>
        {
            if (_info == null)
            {
                (_, _info) = await AppServices.Jellyfin.ProbeServerAsync(_serverUrl);
                OnPropertyChanged(nameof(ServerName));
                OnPropertyChanged(nameof(ServerDetail));
            }
            var usersTask = AppServices.Jellyfin.GetPublicUsersAsync();
            var qcTask = AppServices.Jellyfin.IsQuickConnectEnabledAsync();
            List<UserDto> users;
            try { users = await usersTask; } catch { users = new(); }
            foreach (var u in users) Users.Add(new PublicUserViewModel(this, u, _serverUrl));
            QuickConnectAvailable = await qcTask;
            OnPropertyChanged(nameof(HasUsers));
            if (Users.Count == 0) ShowManualForm = true;
        });
    }

    public async Task SelectUserAsync(PublicUserViewModel u)
    {
        Username = u.Name;
        Password = "";
        ShowManualForm = true;
        if (!u.User.HasPassword) await SignInAsync();
        else PasswordFocusRequested = !PasswordFocusRequested;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(Username)) { Error = "Enter your username."; return; }
        await RunAsync(async () =>
        {
            var user = Username.Trim();
            var password = Password;
            var auth = await AppServices.Jellyfin.AuthenticateAsync(user, password);
            Password = "";
            await FinishAsync(auth, user, password);
        });
    }

    [RelayCommand]
    private async Task StartQuickConnectAsync()
    {
        _qcCts?.Cancel();
        var cts = _qcCts = new CancellationTokenSource();
        await RunAsync(async () =>
        {
            var start = await AppServices.Jellyfin.QuickConnectInitiateAsync(cts.Token);
            QuickConnectCode = start.Code;
            var secret = start.Secret!;
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (!cts.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                await Task.Delay(2500, cts.Token);
                var state = await AppServices.Jellyfin.QuickConnectStateAsync(secret, cts.Token);
                if (state.Authenticated)
                {
                    var auth = await AppServices.Jellyfin.QuickConnectAuthenticateAsync(secret, cts.Token);
                    QuickConnectCode = null;
                    await FinishAsync(auth);
                    return;
                }
            }
            QuickConnectCode = null;
        }, showBusy: false);
    }

    [RelayCommand]
    private void CancelQuickConnect()
    {
        _qcCts?.Cancel();
        QuickConnectCode = null;
    }

    private async Task FinishAsync(AuthenticationResult auth, string? user = null, string? password = null)
    {
        _info ??= new PublicSystemInfo { Id = auth.ServerId, ServerName = ServerName };
        _info.Id ??= auth.ServerId;
        await AppServices.Session.CompleteLoginAsync(_serverUrl, _info, auth, RememberMe);
        _main.ShowShell();
        // Seerr and NostalgiaTV on the same machine get set up in the background.
        _ = Services.IntegrationAutoSetup.RunAsync(_serverUrl, user ?? auth.User?.Name, password);
    }

    [RelayCommand] private void ShowManual() { ShowManualForm = true; Username = ""; }
    [RelayCommand] private void ShowUsers() => ShowManualForm = false;
    [RelayCommand] private void ChangeServer() { _qcCts?.Cancel(); _main.ShowServerSelect(); }
}
