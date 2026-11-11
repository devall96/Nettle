using System.Text.Json;
using Nettle.Core.Api;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.Tests;

/// <summary>Watch together (SyncPlay): the arithmetic and the server messages, on desktop and (by source) Android TV.</summary>
public class SyncPlayTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static SyncPlayService NewService() => new(new JellyfinClient(new HttpClient(), "test", "0"));

    [Fact]
    public void Clock_OffsetAndRoundTrip()
    {
        var t0 = new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Utc);
        // The server is 500 ms behind; 30 ms each way; 2 ms of server work.
        var t1 = t0.AddMilliseconds(30 - 500);
        var t2 = t1.AddMilliseconds(2);
        var t3 = t0.AddMilliseconds(62);
        var (offset, rtt) = SyncPlayMath.ClockSample(t0, t1, t2, t3);
        Assert.Equal(-500, offset.TotalMilliseconds, 3);
        Assert.Equal(60, rtt.TotalMilliseconds, 3);
    }

    [Theory]
    [InlineData(10_000, 10_050, SyncCorrectionKind.None)]   // 50 ms: together
    [InlineData(10_000, 10_600, SyncCorrectionKind.Speed)]  // 600 ms behind: speed up a little
    [InlineData(10_600, 10_000, SyncCorrectionKind.Speed)]  // 600 ms ahead: slow down a little
    [InlineData(10_000, 13_000, SyncCorrectionKind.Skip)]   // 3 s: jump
    public void Drift_Correction(double actual, double expected, SyncCorrectionKind kind) =>
        Assert.Equal(kind, SyncPlayMath.Correction(actual, expected).Kind);

    [Fact]
    public void Drift_SpeedCatchesUpExactly()
    {
        var fix = SyncPlayMath.Correction(10_000, 10_600);
        Assert.True(fix.Speed > 1 && fix.Speed <= 1 + SyncPlayMath.MaxSpeedChange);
        // Playing at that speed for that long gains exactly the gap.
        Assert.Equal(600, (fix.Speed - 1) * fix.DurationMs, 3);
    }

    [Fact]
    public void Expected_PositionAfterUnpause()
    {
        var when = new DateTime(2026, 11, 11, 5, 0, 0, DateTimeKind.Utc);
        var cmd = new SyncPlayCommand("Unpause", when, 120 * 10_000_000L, "e1");
        Assert.Equal(125_000, SyncPlayMath.ExpectedMs(cmd, when.AddSeconds(5)), 3);
        Assert.Equal(120_000, SyncPlayMath.ExpectedMs(cmd, when.AddSeconds(-1)), 3); // not started yet
    }

    [Fact]
    public void Service_FollowsTheGroup()
    {
        var s = NewService();
        SyncPlayQueue? queued = null;
        SyncPlayCommand? command = null;
        s.QueueChanged += q => queued = q;
        s.CommandReceived += c => command = c;

        Assert.True(s.Handle("SyncPlayGroupUpdate", J("""{"GroupId":"g1","Type":"GroupJoined","Data":{"GroupId":"g1","GroupName":"Movie night","State":"Idle","Participants":["sam"]}}""")));
        Assert.True(s.InGroup);
        Assert.Equal("Movie night", s.Group!.Name);

        s.Handle("SyncPlayGroupUpdate", J("""{"GroupId":"g1","Type":"UserJoined","Data":"alex"}"""));
        Assert.Equal(new[] { "sam", "alex" }, s.Group!.Participants);

        s.Handle("SyncPlayGroupUpdate", J("""{"GroupId":"g1","Type":"PlayQueue","Data":{"Reason":"NewPlaylist","Playlist":[{"ItemId":"i1","PlaylistItemId":"e1"},{"ItemId":"i2","PlaylistItemId":"e2"}],"PlayingItemIndex":1,"StartPositionTicks":50000000,"IsPlaying":false}}"""));
        Assert.NotNull(queued);
        Assert.Equal("e2", s.CurrentPlaylistItemId);
        Assert.Equal(50_000_000, queued!.StartPositionTicks);

        s.Handle("SyncPlayCommand", J("""{"GroupId":"g1","PlaylistItemId":"e2","When":"2026-11-11T05:00:00.5Z","PositionTicks":50000000,"Command":"Unpause","EmittedAt":"2026-11-11T05:00:00Z"}"""));
        Assert.Equal("Unpause", command!.Command);
        Assert.Equal(new DateTime(2026, 11, 11, 5, 0, 0, 500, DateTimeKind.Utc), command.When);
        Assert.Same(command, s.LastCommand);

        s.Handle("SyncPlayGroupUpdate", J("""{"GroupId":"g1","Type":"StateUpdate","Data":{"State":"Playing","Reason":"Unpause"}}"""));
        Assert.Equal("Playing", s.State);

        s.Handle("SyncPlayGroupUpdate", J("""{"GroupId":"g1","Type":"GroupLeft","Data":"g1"}"""));
        Assert.False(s.InGroup);
        Assert.Null(s.Queue);
        Assert.False(s.Handle("UserDataChanged", J("{}"))); // not SyncPlay: left for the remote control
    }

    [Fact]
    public void Service_IgnoresCommandsOutsideAGroup()
    {
        var s = NewService();
        SyncPlayCommand? command = null;
        s.CommandReceived += c => command = c;
        s.Handle("SyncPlayCommand", J("""{"Command":"Pause","When":"2026-11-11T05:00:00Z","PositionTicks":0}"""));
        Assert.Null(command);
    }

    [Fact]
    public void TimesGoOutInUtc() => Assert.Equal("2026-11-11T05:00:00.5000000Z", SyncPlayJson.Format(new DateTime(2026, 11, 11, 5, 0, 0, 500, DateTimeKind.Utc)));

    [Fact]
    public void AndroidTv_HasTheSameWatchTogether()
    {
        var kt = ParityTests.KtCore("models/SyncPlay.kt");
        Assert.Contains($"const val TOLERANCE_MS = {SyncPlayMath.ToleranceMs:0}.0", kt);
        Assert.Contains($"const val SKIP_ABOVE_MS = {SyncPlayMath.SkipAboveMs:0}.0", kt);
        Assert.Contains($"const val CATCH_UP_OVER_MS = {SyncPlayMath.CatchUpOverMs:0}.0", kt);
        Assert.Contains($"const val MAX_SPEED_CHANGE = {SyncPlayMath.MaxSpeedChange}", kt);
        var tv = ParityTests.TvFile("player/PlayerModel.kt");
        foreach (var call in new[] { "sync.pause()", "sync.unpause()", "sync.seek(", "sync.next()", "fun applySyncCommand" }) Assert.Contains(call, tv);
        var desk = ParityTests.Desk("ViewModels/PlayerViewModel.cs") + ParityTests.Desk("ViewModels/PlayerViewModel.SyncPlay.cs");
        foreach (var call in new[] { "Sync.PauseAsync()", "Sync.UnpauseAsync()", "Sync.SeekAsync(", "Sync.NextAsync", "public void ApplySyncCommand" }) Assert.Contains(call, desk);
        Assert.Contains("WatchTogetherModel()", ParityTests.TvFile("vm/Shell.kt"));
        Assert.Contains("new WatchTogetherViewModel()", ParityTests.Desk("ViewModels/ShellViewModel.cs"));
    }
}
