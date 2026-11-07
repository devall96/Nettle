using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nettle.Core.Models;

namespace Nettle.UI.ViewModels;

public sealed partial class GuideProgramViewModel(LiveTvViewModel owner, GuideRowViewModel row, NtvProgram? program, DateTimeOffset start, double width, bool isSpacer = false) : ObservableObject
{
    public NtvProgram? Program => program;
    public GuideRowViewModel Row => row;
    /// <summary>Where this cell starts (the program's start, or the gap's).</summary>
    public DateTimeOffset Start => start;
    /// <summary>Invisible filler before the first realized cell of a row (see <see cref="LiveTvViewModel"/>).</summary>
    public bool IsSpacer => isSpacer;
    [ObservableProperty] private double _width = width;
    [ObservableProperty] private bool _isNow;
    public bool IsGap => program == null && !isSpacer;
    public string Title => program?.Title ?? "";
    public string Time => program == null ? "" : $"{program.Start.LocalDateTime:h:mm} – {program.Stop.LocalDateTime:h:mm tt}";
    public bool IsWide => Width > 110;

    partial void OnWidthChanged(double value) => OnPropertyChanged(nameof(IsWide));

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
    /// <summary>The cells currently realized for this row: a spacer, then the programs in the window.</summary>
    public ObservableCollection<GuideProgramViewModel> Programs { get; } = new();

    // Cells are kept per start time so a cell survives re-windowing (keeps focus and its visual).
    internal readonly Dictionary<long, GuideProgramViewModel> Cells = new();
    internal GuideProgramViewModel? Spacer;

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
/// (from now to the end of its schedule, usually days), scrolled horizontally. Rows are
/// virtualized by the view; within each row only the programs around the visible time are
/// turned into cells (a "window" that follows the scroll position), with an invisible spacer
/// keeping them at the right place. That keeps opening and scrolling the guide fast however
/// far ahead NostalgiaTV schedules.
/// </summary>
public sealed partial class LiveTvViewModel : PageViewModel
{
    public const double PxPerMinute = 7;
    public const double ChannelColumnWidth = 200;

    // Realized time window around the scroll position (the view shows about 3 hours).
    private static readonly TimeSpan WindowBefore = TimeSpan.FromHours(2);
    private static readonly TimeSpan WindowAfter = TimeSpan.FromHours(7);
    private static readonly TimeSpan WindowMargin = TimeSpan.FromMinutes(45);

    /// <summary>Total minutes covered by the laid-out guide.</summary>
    public int TotalMinutes { get; private set; } = 180;

    /// <summary>Asks the view to scroll the grid so this x offset (px from guide start) is at the left edge.</summary>
    public event Action<double>? ScrollRequested;
    private double _scrollX;
    private DateTimeOffset _winStart, _winEnd;

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

    protected override Task LoadAsync() => LoadGuideAsync(force: false);

    private async Task LoadGuideAsync(bool force)
    {
        NotConfigured = !AppServices.Ntv.IsConfigured;
        if (NotConfigured) return;
        await RunAsync(async () =>
        {
            // Everything NostalgiaTV publishes (it usually schedules a few days ahead). Cached
            // for a few minutes, so coming back to this page is instant.
            var all = await AppServices.Ntv.LoadGuideAsync(14, force);
            // Channels with nothing scheduled are hidden; re-evaluated on every load, so a channel
            // appears once NostalgiaTV starts scheduling content on it.
            _channels = all.Where(c => c.Programs.Count > 0).ToList();
            Rows.Clear();
            for (int i = 0; i < _channels.Count; i++) Rows.Add(new GuideRowViewModel(_channels[i], i));
            Layout();
            ScrollRequested?.Invoke(NowScrollX);
        });
        // A cached guide that's getting old is refreshed quietly in the background.
        if (!force && AppServices.Ntv.GuideLoadedAt is { } at && DateTimeOffset.Now - at > TimeSpan.FromMinutes(5))
            _ = Task.Run(() => AppServices.Ntv.LoadGuideAsync(14, force: true));
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
        EnsureWindow(WindowStart.AddMinutes(x / PxPerMinute));
    }

    private DateTimeOffset GuideEnd => WindowStart.AddMinutes(TotalMinutes);

    private void Layout()
    {
        // From the current half hour to the last program NostalgiaTV has scheduled.
        var start = AlignDown(DateTimeOffset.Now);
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

        _winStart = _winEnd = default;
        foreach (var row in Rows) { row.Cells.Clear(); row.Programs.Clear(); row.Spacer = null; }
        OnScrolled(_scrollX);
        UpdateNow();

        if (InfoTitle == null && Rows.Count > 0)
        {
            var first = Rows[0].Programs.FirstOrDefault(p => p.IsNow) ?? Rows[0].Programs.FirstOrDefault(p => !p.IsGap && !p.IsSpacer);
            if (first != null) Inspect(first);
        }
    }

    /// <summary>Makes sure cells exist around <paramref name="viewStart"/> (left edge of the view).</summary>
    private void EnsureWindow(DateTimeOffset viewStart)
    {
        if (Rows.Count == 0) return;
        if (_winEnd > _winStart && viewStart - WindowMargin >= _winStart && viewStart + WindowAfter - WindowMargin * 2 <= _winEnd) return;
        var from = viewStart - WindowBefore;
        var to = viewStart + WindowAfter;
        if (from < WindowStart) from = WindowStart;
        if (to > GuideEnd) to = GuideEnd;
        _winStart = from;
        _winEnd = to;
        foreach (var row in Rows) Realize(row, from, to);
    }

    /// <summary>Brings one row's cells in line with the window [from, to), reusing existing cells.</summary>
    private void Realize(GuideRowViewModel row, DateTimeOffset from, DateTimeOffset to)
    {
        var start = WindowStart;
        var end = GuideEnd;
        var now = DateTimeOffset.Now;
        var wanted = new List<GuideProgramViewModel>();
        var cursor = start;

        GuideProgramViewModel Cell(NtvProgram? p, DateTimeOffset s, DateTimeOffset e)
        {
            var key = s.UtcTicks;
            var width = Math.Max(1, (e - s).TotalMinutes * PxPerMinute);
            if (row.Cells.TryGetValue(key, out var c) && c.Program == p)
            {
                c.Width = width;
                return c;
            }
            c = new GuideProgramViewModel(this, row, p, s, width) { IsNow = p != null && p.Start <= now && p.Stop > now };
            row.Cells[key] = c;
            return c;
        }

        // Walk the schedule; gaps between programs become (focusable) "no listing" cells. Only
        // cells overlapping [from, to) are kept; whole programs, so a cell never starts mid-show.
        void Consider(NtvProgram? p, DateTimeOffset s, DateTimeOffset e)
        {
            if (e <= from || s >= to) return;
            wanted.Add(Cell(p, s, e));
        }
        foreach (var p in row.Channel.Programs)
        {
            if (p.Stop <= start) continue;
            if (p.Start >= end) break;
            var s = p.Start < start ? start : p.Start;
            var e = p.Stop > end ? end : p.Stop;
            if (s > cursor) Consider(null, cursor, s);
            Consider(p, s, e);
            cursor = e;
            if (cursor >= to) break;
        }
        if (cursor < end && cursor < to) Consider(null, cursor, end);

        // Spacer: fills the time before the first realized cell.
        var lead = wanted.Count > 0 ? (wanted[0].Start - start).TotalMinutes * PxPerMinute : 0;
        row.Spacer ??= new GuideProgramViewModel(this, row, null, start, 0, isSpacer: true);
        row.Spacer.Width = Math.Max(0, lead);
        wanted.Insert(0, row.Spacer);

        Sync(row.Programs, wanted);
        // Forget cells far outside the window (they'd be recreated if scrolled back to).
        if (row.Cells.Count > wanted.Count * 3)
        {
            var keep = wanted.Select(w => w.Start.UtcTicks).ToHashSet();
            foreach (var k in row.Cells.Keys.Where(k => !keep.Contains(k)).ToList()) row.Cells.Remove(k);
        }
    }

    /// <summary>
    /// Edits <paramref name="target"/> into <paramref name="wanted"/> (both in time order, sharing
    /// cell instances) with removals and inserts at the ends, so cells that stay in view keep
    /// their controls and keyboard focus.
    /// </summary>
    private static void Sync(ObservableCollection<GuideProgramViewModel> target, List<GuideProgramViewModel> wanted)
    {
        var set = wanted.ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--)
            if (!set.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;
            var existing = target.IndexOf(wanted[i]);
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, wanted[i]);
        }
        while (target.Count > wanted.Count) target.RemoveAt(target.Count - 1);
    }

    private void UpdateNow()
    {
        var mins = (DateTimeOffset.Now - WindowStart).TotalMinutes;
        NowVisible = mins >= 0 && mins <= TotalMinutes;
        NowOffset = Math.Clamp(mins, 0, TotalMinutes) * PxPerMinute;
        var now = DateTimeOffset.Now;
        foreach (var row in Rows)
            foreach (var c in row.Programs)
                if (c.Program is { } p) c.IsNow = p.Start <= now && p.Stop > now;
    }

    public void Inspect(GuideProgramViewModel vm)
    {
        if (vm.IsSpacer) return;
        // Keyboard focus moving toward the window's edge pulls more of the schedule in.
        if (vm.Start > _winEnd - WindowMargin || vm.Start < _winStart + WindowMargin)
            EnsureWindowAround(vm.Start);
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

    private void EnsureWindowAround(DateTimeOffset t)
    {
        _winStart = _winEnd = default; // force
        EnsureWindow(t - TimeSpan.FromHours(1));
    }

    public void PlayChannel(GuideRowViewModel row) => AppServices.Main.PlayLive(_channels, row.Index);

    [RelayCommand] private void OpenSettings() => AppServices.Main.Shell?.NavigateRoot(new SettingsViewModel());

    [RelayCommand]
    private Task Reload() => LoadGuideAsync(force: true);
}
