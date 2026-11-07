using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Slopfin.Core.Models;
using Slopfin.UI.Theming;

namespace Slopfin.UI.ViewModels;

/// <summary>Root of the app: splash → login flow → shell, plus the player layered on top.</summary>
public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase? _root;
    [ObservableProperty] private PlayerViewModel? _player;
    [ObservableProperty] private bool _isStarting = true;
    [ObservableProperty] private string _startupText = "Starting…";
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private bool _isToastVisible;

    public ShellViewModel? Shell => Root as ShellViewModel;
    public bool IsPlayerOpen => Player != null;

    private CancellationTokenSource? _toastCts;

    public MainViewModel()
    {
        AppServices.Main = this;
    }

    partial void OnPlayerChanged(PlayerViewModel? value) => OnPropertyChanged(nameof(IsPlayerOpen));

    public async Task StartAsync()
    {
        ThemeManager.Apply(AppServices.Settings.Current.Theme);
        await AppServices.Accounts.LoadAsync();
        await AppServices.LoadIntegrationsAsync();
        _ = MaybeAutoCheckUpdatesAsync();

#if DEBUG
        // Developer convenience: SLOPFIN_DEV_SERVER / SLOPFIN_DEV_USER sign in without saving anything.
        var devServer = Environment.GetEnvironmentVariable("SLOPFIN_DEV_SERVER");
        if (!string.IsNullOrEmpty(devServer))
        {
            try
            {
                var (url, info) = await AppServices.Jellyfin.ProbeServerAsync(devServer);
                AppServices.Session.BeginLogin(url);
                var auth = await AppServices.Jellyfin.AuthenticateAsync(Environment.GetEnvironmentVariable("SLOPFIN_DEV_USER") ?? "demo", "");
                await AppServices.Session.CompleteLoginAsync(url, info, auth, remember: false);
                ShowShell();
                IsStarting = false;
                return;
            }
            catch { /* fall through to normal startup */ }
        }
#endif

        var auto = AppServices.Session.AutoLoginCandidate;
        if (auto != null)
        {
            StartupText = $"Signing in as {auto.UserName}…";
            try
            {
                if (await AppServices.Session.ResumeAsync(auto))
                {
                    ShowShell();
                    IsStarting = false;
                    return;
                }
                ShowLogin(auto.ServerUrl, prefillUser: auto.UserName, message: "Your session expired. Please sign in again.");
                IsStarting = false;
                return;
            }
            catch
            {
                // Offline or server unreachable: fall through to the account picker.
            }
        }

        ShowAccountPicker();
        IsStarting = false;
    }

    /// <summary>Opt-in only: one quiet check per launch against the user's own update source.</summary>
    private async Task MaybeAutoCheckUpdatesAsync()
    {
        var s = AppServices.Settings.Current;
        var up = AppServices.Updater;
        if (!s.AutoCheckUpdates || string.IsNullOrWhiteSpace(s.UpdateSource) || up == null || !up.IsInstalled) return;
        try
        {
            await Task.Delay(8000);
            var v = await up.CheckAsync(s.UpdateSource);
            if (v != null) Toast($"Slopfin {v} is available. Install it from Settings → Updates.", 6000);
        }
        catch (Exception ex) { Slopfin.Core.Services.Log.Warn("auto update check failed: " + ex.Message); }
    }

    public void ShowAccountPicker()
    {
        if (AppServices.Session.SavedAccounts.Count == 0) Root = new ServerSelectViewModel(this);
        else Root = new AccountPickerViewModel(this);
    }

    public void ShowServerSelect() => Root = new ServerSelectViewModel(this);

    public void ShowLogin(string serverUrl, PublicSystemInfo? info = null, string? prefillUser = null, string? message = null) =>
        Root = new LoginViewModel(this, serverUrl, info, prefillUser, message);

    public void ShowShell()
    {
        var shell = new ShellViewModel(this);
        Root = shell;
        OnPropertyChanged(nameof(Shell));
        _ = shell.InitializeAsync();
        MaybeOfferMenuEntry();
#if DEBUG
        // Developer convenience: SLOPFIN_DEV_PLAY=<itemId> starts playing that item after sign-in.
        var devPlay = Environment.GetEnvironmentVariable("SLOPFIN_DEV_PLAY");
        if (!string.IsNullOrEmpty(devPlay))
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                var item = await AppServices.Jellyfin.GetItemAsync(devPlay);
                await Dispatcher.UIThread.InvokeAsync(() => PlayItem(item, resume: true));
            });
        // SLOPFIN_DEV_PAGE=livetv|settings|requests opens that page after sign-in.
        PageViewModel? devPage = Environment.GetEnvironmentVariable("SLOPFIN_DEV_PAGE") switch
        {
            "livetv" => new LiveTvViewModel(),
            "settings" => new SettingsViewModel(),
            "requests" => new RequestsViewModel(),
            _ => null,
        };
        if (devPage != null)
            Dispatcher.UIThread.Post(() => shell.NavigateRoot(devPage), DispatcherPriority.Background);
        // SLOPFIN_DEV_LIVE=<channel number> tunes that NostalgiaTV channel after sign-in.
        var devLive = Environment.GetEnvironmentVariable("SLOPFIN_DEV_LIVE");
        if (!string.IsNullOrEmpty(devLive))
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                var channels = (await AppServices.Ntv.LoadGuideAsync(1)).Where(c => c.Programs.Count > 0).ToList();
                var index = Math.Max(0, channels.FindIndex(c => c.Number == devLive));
                await Dispatcher.UIThread.InvokeAsync(() => PlayLive(channels, index));
            });
#endif
    }

    // ---------- "Add to applications menu?" (Linux AppImage, asked once) ----------

    [ObservableProperty] private bool _isMenuPromptVisible;

    private async void MaybeOfferMenuEntry()
    {
        var d = AppServices.Desktop;
        if (d == null || !d.OfferOnFirstRun || d.IsInMenu || AppServices.Settings.Current.MenuPromptAnswered) return;
        await Task.Delay(1500); // let the home screen settle first
        IsMenuPromptVisible = true;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task AcceptMenuPrompt()
    {
        IsMenuPromptVisible = false;
        AppServices.Settings.Current.MenuPromptAnswered = true;
        await AppServices.Settings.SaveAsync();
        try
        {
            AppServices.Desktop?.AddToMenu();
            Toast("Slopfin is now in your applications menu");
        }
        catch (Exception ex)
        {
            Slopfin.Core.Services.Log.Error("add to menu failed", ex);
            Toast("Couldn't add Slopfin to the menu: " + ex.Message, 5000);
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task DeclineMenuPrompt()
    {
        IsMenuPromptVisible = false;
        AppServices.Settings.Current.MenuPromptAnswered = true;
        await AppServices.Settings.SaveAsync();
        Toast("You can add it any time in Settings → Desktop", 4000);
    }

    public void SwitchUser()
    {
        ClosePlayer();
        AppServices.Session.SwitchUser();
        ShowAccountPicker();
        OnPropertyChanged(nameof(Shell));
    }

    // ---------- Playback ----------

    public void PlayItem(BaseItemDto item, bool resume, string? mediaSourceId = null)
    {
        ClosePlayer();
        long? start = resume && item.UserData?.PlaybackPositionTicks is > 0 ? item.UserData.PlaybackPositionTicks : null;
        Player = PlayerViewModel.ForItem(this, item, start, mediaSourceId);
    }

    public void PlayLive(IReadOnlyList<NtvChannel> channels, int index)
    {
        ClosePlayer();
        Player = PlayerViewModel.ForLive(this, channels, index);
    }

    public void ClosePlayer()
    {
        var p = Player;
        if (p == null) return;
        Player = null;
        p.Dispose();
        // Refresh once the server has the final position, so Continue Watching shows it.
        _ = RefreshAfterAsync(p.Stopped);
    }

    private async Task RefreshAfterAsync(Task stopped)
    {
        await Task.WhenAny(stopped, Task.Delay(2500));
        Shell?.RefreshCurrent();
    }

    /// <summary>
    /// App is closing: stop playback and wait (briefly) until the server has the resume position,
    /// so closing the window mid-episode never loses where you were.
    /// </summary>
    public async Task ShutdownAsync()
    {
        var p = Player;
        if (p == null) return;
        Player = null;
        p.Dispose();
        if (p.IsLive) return; // live TV has no resume position
        await Task.WhenAny(p.Stopped, Task.Delay(3000));
        Slopfin.Core.Services.Log.Info(p.Stopped.IsCompleted ? "resume position saved before exit" : "resume report timed out at exit");
    }

    /// <summary>Another copy of Slopfin was started: bring this one forward instead.</summary>
    public void ActivatedByOtherInstance() => Toast("Slopfin is already open");

    /// <summary>Settings → Reduce motion. Off = page slides, fades and card lifts are skipped.</summary>
    [ObservableProperty] private bool _motionEnabled = !AppServices.Settings.Current.ReduceMotion;

    // ---------- Toasts ----------

    public async void Toast(string text, int ms = 2600)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        ToastText = text;
        IsToastVisible = true;
        try { await Task.Delay(ms, cts.Token); } catch (TaskCanceledException) { return; }
        await Dispatcher.UIThread.InvokeAsync(() => IsToastVisible = false);
    }
}
