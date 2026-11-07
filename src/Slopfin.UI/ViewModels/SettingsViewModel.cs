using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Models;
using Slopfin.UI.Theming;

namespace Slopfin.UI.ViewModels;

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
        ? "Used by Slopfin: skip buttons" : "Server-side";
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

    public string VersionText => $"Slopfin {AppServices.Version}  •  {AppServices.Platform.PlatformName}";
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
    public decimal NetworkCaching { get => P.NetworkCachingMs; set { P.NetworkCachingMs = (int)value; Save(); OnPropertyChanged(); } }
    public decimal SeekStep { get => P.SeekStepSeconds; set { P.SeekStepSeconds = (int)value; Save(); OnPropertyChanged(); } }

    public SegmentChoice IntroChoice { get => Choice(P.IntroAction); set { P.IntroAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice RecapChoice { get => Choice(P.RecapAction); set { P.RecapAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice CreditsChoice { get => Choice(P.CreditsAction); set { P.CreditsAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice PreviewChoice { get => Choice(P.PreviewAction); set { P.PreviewAction = value.Value; Save(); OnPropertyChanged(); } }
    public SegmentChoice CommercialChoice { get => Choice(P.CommercialAction); set { P.CommercialAction = value.Value; Save(); OnPropertyChanged(); } }
    private SegmentChoice Choice(SegmentAction a) => SegmentChoices.First(c => c.Value == a);

    public bool NtvDirectPlay { get => P.NtvDirectPlay; set { P.NtvDirectPlay = value; Save(); OnPropertyChanged(); } }

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
        if (string.IsNullOrWhiteSpace(S.UpdateSource)) { UpdateStatus = "Enter an update source first (e.g. your GitHub repo URL)."; return; }
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
            var ch = await AppServices.Ntv.LoadGuideAsync(1);
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
