using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;
using Nettle.UI.Theming;

namespace Nettle.UI.ViewModels;

public sealed partial class ThemeOptionViewModel(SettingsViewModel owner, ThemePalette palette) : ObservableObject
{
    public ThemePalette Palette => palette;
    public string Name => palette.Name;
    public string Tagline => palette.Tagline;
    public IBrush Background => new SolidColorBrush(palette.Background);
    public IBrush Surface => new SolidColorBrush(palette.SurfaceHigh);
    public IBrush Accent => new SolidColorBrush(palette.Accent);
    public IBrush Accent2 => new SolidColorBrush(palette.Accent2);
    public IBrush Text => new SolidColorBrush(palette.Text);
    public IBrush Muted => new SolidColorBrush(palette.TextMuted);
    [ObservableProperty] private bool _isSelected;
    [RelayCommand] private void Apply() => owner.ApplyTheme(this);
}

public sealed record SegmentChoice(SegmentAction Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class PluginRowViewModel(PluginInfo p)
{
    public string Name => p.Name ?? "";
    public string Version => p.Version ?? "";
    public string Status => p.Status ?? "";
    public string Support => (p.Name ?? "").Contains("intro", StringComparison.OrdinalIgnoreCase) || (p.Name ?? "").Contains("segment", StringComparison.OrdinalIgnoreCase)
        ? "Used by Nettle: skip buttons" : "Server-side";
}

public sealed partial class SettingsViewModel : PageViewModel
{
    public override string NavKey => "settings";
    private AppSettings S => AppServices.Settings.Current;
    private PlaybackSettings P => S.Playback;

    public ObservableCollection<ThemeOptionViewModel> Themes { get; } = new();
    public ObservableCollection<AccountTileViewModel> Accounts { get; } = new();
    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    public IReadOnlyList<SegmentChoice> SegmentChoices { get; } = new List<SegmentChoice>
    {
        new(SegmentAction.ShowButton, "Show skip button"),
        new(SegmentAction.AutoSkip, "Skip automatically"),
        new(SegmentAction.Off, "Do nothing"),
    };

    public string VersionText => $"Nettle {AppServices.Version}  •  {AppServices.Platform.PlatformName}";
    public string ServerText => $"{AppServices.Session.Current?.ServerName}  •  {AppServices.Session.Current?.ServerUrl}";
    public string UserText => AppServices.Session.Current?.UserName ?? "";

    // Integrations
    [ObservableProperty] private string _seerrUrl = "";
    [ObservableProperty] private string _seerrApiKey = "";
    [ObservableProperty] private string _seerrUser = "";
    [ObservableProperty] private string _seerrPassword = "";
    [ObservableProperty] private string? _seerrStatus;
    [ObservableProperty] private string _ntvUrl = "";
    [ObservableProperty] private string _ntvApiKey = "";
    [ObservableProperty] private string? _ntvStatus;
    [ObservableProperty] private bool _pluginsLoaded;

    public SettingsViewModel()
    {
        foreach (var t in ThemeManager.All)
            Themes.Add(new ThemeOptionViewModel(this, t) { IsSelected = t.Key == ThemeManager.Current.Key });
        _seerrUrl = S.SeerrUrl ?? "";
        _ntvUrl = S.NtvUrl ?? "";
    }

    protected override async Task LoadAsync()
    {
        SeerrApiKey = await AppServices.Secure.GetAsync("seerr.apikey") ?? "";
        NtvApiKey = await AppServices.Secure.GetAsync("ntv.apikey") ?? "";
        if (AppServices.Seerr.IsConfigured) SeerrStatus = "Connected";
        if (AppServices.Ntv.IsConfigured) NtvStatus = "Configured";
        var picker = new AccountPickerViewModel(AppServices.Main);
        foreach (var a in picker.Accounts) Accounts.Add(a);
        _ = LoadPluginsAsync();
    }

    private async Task LoadPluginsAsync()
    {
        try
        {
            var list = await AppServices.Jellyfin.GetPluginsAsync();
            foreach (var p in list.OrderBy(p => p.Name)) Plugins.Add(new PluginRowViewModel(p));
            PluginsLoaded = true;
        }
        catch { PluginsLoaded = false; /* non-admins can't list plugins */ }
    }

    private void Save() => _ = AppServices.Settings.SaveAsync();

    public void ApplyTheme(ThemeOptionViewModel t)
    {
        foreach (var x in Themes) x.IsSelected = x == t;
        ThemeManager.Apply(t.Palette.Key);
        S.Theme = t.Palette.Key;
        Save();
    }

    // ---------- Playback ----------
    public bool HardwareDecoding { get => P.HardwareDecoding; set { P.HardwareDecoding = value; Save(); OnPropertyChanged(); } }
    public bool AutoPlayNext { get => P.AutoPlayNextEpisode; set { P.AutoPlayNextEpisode = value; Save(); OnPropertyChanged(); } }
    public string AudioLanguage { get => P.PreferredAudioLanguage; set { P.PreferredAudioLanguage = value.Trim(); Save(); OnPropertyChanged(); } }
    public string SubtitleLanguage { get => P.PreferredSubtitleLanguage; set { P.PreferredSubtitleLanguage = value.Trim(); Save(); OnPropertyChanged(); } }

    /// <summary>When subtitles turn on by themselves (Jellyfin's subtitle modes).</summary>
    public IReadOnlyList<ChoiceOption> SubtitleModes { get; } =
    [
        new("Same as my Jellyfin profile", ""),
        new("Default (the file's default track)", "Default"),
        new("Always on", "Always"),
        new("Only forced (foreign dialogue)", "OnlyForced"),
        new("Smart (when audio is in another language)", "Smart"),
        new("Off", "None"),
    ];

    public ChoiceOption SubtitleModeChoice
    {
        get => SubtitleModes.FirstOrDefault(m => m.Value == P.SubtitleMode) ?? SubtitleModes[0];
        set { P.SubtitleMode = value?.Value ?? ""; Save(); OnPropertyChanged(); }
    }
    public decimal NetworkBuffer { get => P.NetworkBufferSeconds; set { P.NetworkBufferSeconds = (int)value; Save(); OnPropertyChanged(); } }
    public decimal SeekStep { get => P.SeekStepSeconds; set { P.SeekStepSeconds = (int)value; Save(); OnPropertyChanged(); } }
    public bool AudioPassthrough { get => P.AudioPassthrough; set { P.AudioPassthrough = value; Save(); OnPropertyChanged(); } }
    public string PassthroughHint => OperatingSystem.IsWindows()
        ? "Sends Dolby (incl. Atmos and TrueHD) and DTS (incl. DTS-HD and DTS:X) untouched to your AV receiver or soundbar over HDMI / S/PDIF. While it plays, Nettle's volume control has no effect. For TrueHD and DTS-HD only, other apps' sound is paused (Windows needs exclusive use of the device for those). Leave off for TV speakers and headphones."
        : "Sends Dolby (incl. Atmos and TrueHD) and DTS (incl. DTS-HD and DTS:X) untouched to your AV receiver or soundbar over HDMI / S/PDIF. Your sound server must allow passthrough for that output. Nettle's volume control has no effect on it. Leave off for TV speakers and headphones.";

    public bool StillWatchingPrompt
    {
        get => P.StillWatchingPrompt;
        set { P.StillWatchingPrompt = value; Save(); OnPropertyChanged(); }
    }
    public decimal StillWatchingEpisodes { get => P.StillWatchingEpisodes; set { P.StillWatchingEpisodes = Math.Clamp((int)value, 1, 20); Save(); OnPropertyChanged(); } }

    // ---------- Appearance ----------
    public bool ReduceMotion
    {
        get => S.ReduceMotion;
        set
        {
            S.ReduceMotion = value;
            AppServices.Main.MotionEnabled = !value;
            Save();
            OnPropertyChanged();
        }
    }

    public string SecureStoreText => OperatingSystem.IsWindows()
        ? "Saved profiles on this PC (tokens encrypted with Windows DPAPI):"
        : "Saved profiles on this PC (tokens kept in your desktop keyring):";

    public SegmentChoice IntroChoice { get => Choice(P.IntroAction); set { P.IntroAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice RecapChoice { get => Choice(P.RecapAction); set { P.RecapAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice CreditsChoice { get => Choice(P.CreditsAction); set { P.CreditsAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice PreviewChoice { get => Choice(P.PreviewAction); set { P.PreviewAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice CommercialChoice { get => Choice(P.CommercialAction); set { P.CommercialAction = value.Value; Save(); OnPropertyChanged(); } }
    private SegmentChoice Choice(SegmentAction a) => SegmentChoices.First(c => c.Value == a);

    public bool NtvDirectPlay { get => P.NtvDirectPlay; set { P.NtvDirectPlay = value; Save(); OnPropertyChanged(); } }

    // ---------- Desktop (Linux applications menu) ----------
    public bool HasDesktopIntegration => AppServices.Desktop != null;
    public bool IsInAppMenu => AppServices.Desktop?.IsInMenu == true;
    public string AppMenuStatus => IsInAppMenu ? "Nettle is in your applications menu." : "Nettle isn't in your applications menu.";

    [RelayCommand]
    private void ToggleAppMenu()
    {
        var d = AppServices.Desktop;
        if (d == null) return;
        try
        {
            if (d.IsInMenu) d.RemoveFromMenu(); else d.AddToMenu();
            S.MenuPromptAnswered = true;
            Save();
        }
        catch (Exception ex) { AppServices.Main.Toast("Couldn't update the menu entry: " + ex.Message, 5000); }
        OnPropertyChanged(nameof(IsInAppMenu));
        OnPropertyChanged(nameof(AppMenuStatus));
        OnPropertyChanged(nameof(AppMenuButtonText));
    }

    public string AppMenuButtonText => IsInAppMenu ? "Remove from menu" : "Add to applications menu";

    // ---------- Updates (opt-in, user-chosen source only) ----------
    public string UpdateSource { get => S.UpdateSource; set { S.UpdateSource = value?.Trim() ?? ""; Save(); OnPropertyChanged(); } }
    public bool AutoCheckUpdates { get => S.AutoCheckUpdates; set { S.AutoCheckUpdates = value; Save(); OnPropertyChanged(); } }
    public bool IsInstalledApp => AppServices.Updater?.IsInstalled == true;
    public string InstallKind => IsInstalledApp ? $"Installed version {AppServices.Version}" : $"Version {AppServices.Version} (portable copy: updates need the installed app)";
    [ObservableProperty] private string? _updateStatus;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isUpdating;

    [RelayCommand]
    private async Task CheckUpdates()
    {
        if (AppServices.Updater == null) { UpdateStatus = "Updates aren't supported on this platform."; return; }
        if (string.IsNullOrWhiteSpace(S.UpdateSource)) { UpdateStatus = "Enter an update source first (Nettle's own is https://github.com/devall96/Nettle)."; return; }
        UpdateStatus = "Checking…";
        IsUpdateAvailable = false;
        try
        {
            var v = await AppServices.Updater.CheckAsync(S.UpdateSource);
            IsUpdateAvailable = v != null;
            UpdateStatus = v == null ? $"You're up to date ({AppServices.Version})." : $"Version {v} is available.";
        }
        catch (Exception ex) { UpdateStatus = "Couldn't check: " + ex.Message; }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (AppServices.Updater == null) return;
        IsUpdating = true;
        try
        {
            await AppServices.Updater.DownloadAndRestartAsync(p => Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateStatus = $"Downloading… {p}%"));
        }
        catch (Exception ex) { UpdateStatus = "Update failed: " + ex.Message; IsUpdating = false; }
    }

    public bool AllowDirectTmdbImages
    {
        get => S.AllowDirectTmdbImages;
        set { S.AllowDirectTmdbImages = value; AppServices.Seerr.AllowDirectTmdbImages = value; Save(); OnPropertyChanged(); }
    }

    public bool AutoLogin { get => S.AutoLogin; set { S.AutoLogin = value; Save(); OnPropertyChanged(); } }

    // ---------- Integrations ----------
    [RelayCommand]
    private async Task SaveSeerr()
    {
        SeerrStatus = "Checking…";
        try
        {
            var url = string.IsNullOrWhiteSpace(SeerrUrl) ? null : Core.Api.SeerrClient.NormalizeUrl(SeerrUrl);
            S.SeerrUrl = url;
            Save();
            if (url == null)
            {
                await AppServices.Secure.RemoveAsync("seerr.apikey");
                await AppServices.Secure.RemoveAsync("seerr.cookie");
                AppServices.Seerr.Configure(null, null, null);
                SeerrStatus = "Removed";
                return;
            }

            if (!string.IsNullOrWhiteSpace(SeerrUser))
            {
                await AppServices.Seerr.SignInWithJellyfinAsync(url, SeerrUser.Trim(), SeerrPassword);
                SeerrPassword = "";
                var cookie = AppServices.Seerr.SessionCookie;
                if (cookie != null) await AppServices.Secure.SetAsync("seerr.cookie", cookie);
                await AppServices.Secure.RemoveAsync("seerr.apikey");
                SeerrApiKey = "";
            }
            else
            {
                await AppServices.Secure.SetAsync("seerr.apikey", SeerrApiKey.Trim());
                AppServices.Seerr.Configure(url, SeerrApiKey, null);
            }
            var me = await AppServices.Seerr.GetMeAsync();
            SeerrStatus = $"Connected as {me.DisplayName ?? me.Username ?? me.JellyfinUsername}";
        }
        catch (Exception ex) { SeerrStatus = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SaveNtv()
    {
        NtvStatus = "Checking…";
        try
        {
            S.NtvUrl = string.IsNullOrWhiteSpace(NtvUrl) ? null : NtvUrl.Trim();
            Save();
            if (string.IsNullOrWhiteSpace(NtvApiKey)) await AppServices.Secure.RemoveAsync("ntv.apikey");
            else await AppServices.Secure.SetAsync("ntv.apikey", NtvApiKey.Trim());
            AppServices.Ntv.Configure(S.NtvUrl, NtvApiKey);
            if (!AppServices.Ntv.IsConfigured) { NtvStatus = "Removed"; return; }
            var ch = await AppServices.Ntv.LoadGuideAsync(14, force: true);
            NtvStatus = $"Connected: {ch.Count} channels, {ch.Sum(c => c.Programs.Count):N0} listings";
        }
        catch (Exception ex) { NtvStatus = "Failed: " + ex.Message; }
    }

    [RelayCommand] private void SwitchUser() => AppServices.Main.SwitchUser();

    [RelayCommand]
    private void ClearImageCache()
    {
        AppServices.Images.ClearDisk();
        AppServices.Main.Toast("Image cache cleared");
    }
}
