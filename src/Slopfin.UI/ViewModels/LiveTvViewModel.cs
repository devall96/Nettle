using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Slopfin.Core.Models;

namespace Slopfin.UI.ViewModels;

public sealed partial class GuideProgramViewModel(LiveTvViewModel owner, GuideRowViewModel row, NtvProgram? program, double width, bool isNow) : ObservableObject
{
    public NtvProgram? Program => program;
    public GuideRowViewModel Row => row;
    public double Width => width;
    public bool IsNow => isNow;
    public bool IsGap => program == null;
    public string Title => program?.Title ?? "";
    public string Time => program == null ? "" : $"{program.Start.LocalDateTime:h:mm} – {program.Stop.LocalDateTime:h:mm tt}";
    public bool IsWide => width > 110;

    [RelayCommand] private void Focus() => owner.Inspect(this);
    [RelayCommand] private void Play() => owner.PlayChannel(row);
}

public sealed partial class GuideRowViewModel : ObservableObject
{
    public NtvChannel Channel { get; }
    public int Index { get; }
    public string Name => Channel.Name;
    public string? Number => Channel.Number;
    public string? LogoUrl => Channel.LogoUrl;
    public string Initials => Channel.Name.Length >= 2 ? Channel.Name[..2].ToUpperInvariant() : Channel.Name;
    [ObservableProperty] private IReadOnlyList<GuideProgramViewModel> _programs = Array.Empty<GuideProgramViewModel>();

    public GuideRowViewModel(NtvChannel channel, int index)
    {
        Channel = channel;
        Index = index;
    }
}

public sealed partial class TimeSlotViewModel(string label, double width)
{
    public string Label => label;
    public double Width => width;
}

/// <summary>
/// NostalgiaTV live guide: a classic cable-style grid covering everything NostalgiaTV publishes
/// (from a little before now to the end of its schedule), scrolled horizontally. Rows are
/// virtualized by the view, so only visible channels are realized.
/// </summary>
public sealed partial class LiveTvViewModel : PageViewModel
{
    public const double PxPerMinute = 7;
    public const double ChannelColumnWidth = 200;
    /// <summary>
    /// How much of the past the guide shows. NostalgiaTV's XMLTV only lists programs that haven't
    /// ended yet, so earlier time would just be empty; the current half hour is enough (programs
    /// already on air still show from their start).
    /// </summary>
    private const int PastMinutes = 0;

    /// <summary>Total minutes covered by the laid-out guide.</summary>
    public int TotalMinutes { get; private set; } = 180;

    /// <summary>Asks the view to scroll the grid so this x offset (px from guide start) is at the left edge.</summary>
    public event Action<double>? ScrollRequested;
    private double _scrollX;

    public override string NavKey => "livetv";
    public ObservableCollection<GuideRowViewModel> Rows { get; } = new();
    public ObservableCollection<TimeSlotViewModel> Slots { get; } = new();
    private List<NtvChannel> _channels = new();
    private readonly DispatcherTimer _clock;

    [ObservableProperty] private DateTimeOffset _windowStart;
    [ObservableProperty] private double _nowOffset;
    [ObservableProperty] private bool _nowVisible;
    [ObservableProperty] private string _dayLabel = "";
    [ObservableProperty] private bool _notConfigured;

    // Info panel for the focused program.
    [ObservableProperty] private string? _infoTitle;
    [ObservableProperty] private string? _infoSub;
    [ObservableProperty] private string? _infoTime;
    [ObservableProperty] private string? _infoDesc;
    [ObservableProperty] private string? _infoChannel;
    [ObservableProperty] private string? _infoImage;
    [ObservableProperty] private double _infoProgress;

    public double GridWidth => TotalMinutes * PxPerMinute;

    public LiveTvViewModel()
    {
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => UpdateNow();
    }

    protected override async Task LoadAsync()
    {
        NotConfigured = !AppServices.Ntv.IsConfigured;
        if (NotConfigured) return;
        await RunAsync(async () =>
        {
            _channels = await LoadChannelsWithContentAsync();
            Rows.Clear();
            for (int i = 0; i < _channels.Count; i++) Rows.Add(new GuideRowViewModel(_channels[i], i));
            Layout();
            ScrollRequested?.Invoke(NowScrollX);
        });
    }

    /// <summary>
    /// Channels with nothing scheduled are hidden. This is re-evaluated on every load, so a
    /// channel appears automatically once NostalgiaTV starts scheduling content on it.
    /// </summary>
    private static async Task<List<NtvChannel>> LoadChannelsWithContentAsync()
    {
        // Everything NostalgiaTV publishes (it usually schedules a few days ahead).
        var all = await AppServices.Ntv.LoadGuideAsync(14);
        return all.Where(c => c.Programs.Count > 0).ToList();
    }

    protected override void OnActivated() => _clock.Start();
    public override void OnDeactivated() => _clock.Stop();

    private static DateTimeOffset AlignDown(DateTimeOffset t) =>
        new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute >= 30 ? 30 : 0, 0, t.Offset);

    /// <summary>X offset of the current half hour, so "now" sits near the left of the view.</summary>
    private double NowScrollX => Math.Max(0, ((AlignDown(DateTimeOffset.Now) - WindowStart).TotalMinutes) * PxPerMinute);

    [RelayCommand]
    private void JumpToNow()
    {
        if (Slots.Count == 0) Layout();
        ScrollRequested?.Invoke(NowScrollX);
    }

    [RelayCommand] private void Later() => ScrollRequested?.Invoke(_scrollX + 90 * PxPerMinute);
    [RelayCommand] private void Earlier() => ScrollRequested?.Invoke(Math.Max(0, _scrollX - 90 * PxPerMinute));

    /// <summary>The view reports its horizontal scroll position (px from guide start).</summary>
    public void OnScrolled(double x)
    {
        _scrollX = x;
        var t = WindowStart.AddMinutes(x / PxPerMinute + 1);
        DayLabel = t.LocalDateTime.Date == DateTime.Today ? "Today"
            : t.LocalDateTime.Date == DateTime.Today.AddDays(1) ? "Tomorrow"
            : t.LocalDateTime.ToString("dddd, MMM d");
    }

    private void Layout()
    {
        // From a little before now to the last program NostalgiaTV has scheduled.
        var start = AlignDown(DateTimeOffset.Now.AddMinutes(-PastMinutes));
        var lastStop = _channels.SelectMany(c => c.Programs).Select(p => p.Stop).DefaultIfEmpty(start.AddHours(3)).Max();
        var end = AlignDown(lastStop.AddMinutes(29));
        if (end <= start.AddHours(3)) end = start.AddHours(3);
        WindowStart = start;
        TotalMinutes = (int)(end - start).TotalMinutes;
        OnPropertyChanged(nameof(GridWidth));

        Slots.Clear();
        for (int m = 0; m < TotalMinutes; m += 30)
        {
            var t = start.AddMinutes(m).LocalDateTime;
            // Midnight gets the date so it's clear when the day changes.
            var label = t.Hour == 0 && t.Minute == 0 ? t.ToString("ddd MMM d") : t.ToString("h:mm tt");
            Slots.Add(new TimeSlotViewModel(label, 30 * PxPerMinute));
        }
        OnScrolled(_scrollX);

        var now = DateTimeOffset.Now;
        foreach (var row in Rows)
        {
            var list = new List<GuideProgramViewModel>();
            var cursor = start;
            foreach (var p in row.Channel.Programs)
            {
                if (p.Stop <= start) continue;
                if (p.Start >= end) break;
                var s = p.Start < start ? start : p.Start;
                var e = p.Stop > end ? end : p.Stop;
                if (s > cursor) list.Add(new GuideProgramViewModel(this, row, null, (s - cursor).TotalMinutes * PxPerMinute, false));
                list.Add(new GuideProgramViewModel(this, row, p, Math.Max(1, (e - s).TotalMinutes * PxPerMinute), p.Start <= now && p.Stop > now));
                cursor = e;
            }
            if (cursor < end)
                list.Add(new GuideProgramViewModel(this, row, null, (end - cursor).TotalMinutes * PxPerMinute, false));
            row.Programs = list;
        }
        UpdateNow();

        if (InfoTitle == null && Rows.Count > 0)
        {
            var first = Rows[0].Programs.FirstOrDefault(p => p.IsNow) ?? Rows[0].Programs.FirstOrDefault(p => !p.IsGap);
            if (first != null) Inspect(first);
        }
    }

    private void UpdateNow()
    {
        var mins = (DateTimeOffset.Now - WindowStart).TotalMinutes;
        NowVisible = mins >= 0 && mins <= TotalMinutes;
        NowOffset = Math.Clamp(mins, 0, TotalMinutes) * PxPerMinute;
    }

    public void Inspect(GuideProgramViewModel vm)
    {
        var p = vm.Program;
        InfoChannel = (vm.Row.Number != null ? vm.Row.Number + "  " : "") + vm.Row.Name;
        if (p == null)
        {
            InfoTitle = "No listing";
            InfoSub = InfoDesc = InfoImage = null;
            InfoTime = null;
            InfoProgress = 0;
            return;
        }
        InfoTitle = p.Title;
        InfoSub = string.Join("  •  ", new[] { p.EpisodeNum, p.SubTitle, p.Category }.Where(s => !string.IsNullOrEmpty(s)));
        InfoTime = $"{p.Start.LocalDateTime:ddd h:mm tt} – {p.Stop.LocalDateTime:h:mm tt}  ({(int)p.Duration.TotalMinutes} min)";
        InfoDesc = p.Description;
        InfoImage = p.IconUrl ?? vm.Row.LogoUrl;
        var now = DateTimeOffset.Now;
        InfoProgress = now >= p.Start && now < p.Stop ? (now - p.Start) / p.Duration : 0;
    }

    public void PlayChannel(GuideRowViewModel row) => AppServices.Main.PlayLive(_channels, row.Index);

    [RelayCommand] private void OpenSettings() => AppServices.Main.Shell?.NavigateRoot(new SettingsViewModel());

    [RelayCommand]
    private async Task Reload()
    {
        NotConfigured = !AppServices.Ntv.IsConfigured;
        if (NotConfigured) return;
        await RunAsync(async () =>
        {
            _channels = await LoadChannelsWithContentAsync();
            Rows.Clear();
            for (int i = 0; i < _channels.Count; i++) Rows.Add(new GuideRowViewModel(_channels[i], i));
            Layout();
            ScrollRequested?.Invoke(NowScrollX);
        });
    }
}
