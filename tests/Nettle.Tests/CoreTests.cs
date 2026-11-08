using System.Text.Json;
using System.Text.RegularExpressions;
using Nettle.Core.Api;
using Nettle.Core.Json;
using Nettle.Core.Models;
using Nettle.Core.Platform;
using Nettle.Core.Services;

namespace Nettle.Tests;

public class XmltvTests
{
    [Theory]
    [InlineData("20261107203000 +0000", 2026, 11, 7, 20, 30, 0)]
    [InlineData("20261107203000 -0500", 2026, 11, 7, 20, 30, -5)]
    [InlineData("202611072030", 2026, 11, 7, 20, 30, 0)]
    public void ParsesXmltvTimes(string v, int y, int mo, int d, int h, int mi, int offsetHours)
    {
        var t = NtvClient.ParseXmltvTime(v);
        Assert.NotNull(t);
        Assert.Equal(new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(offsetHours)), t);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsBadTimes(string? v) => Assert.Null(NtvClient.ParseXmltvTime(v));

    [Theory]
    [InlineData("0.2.0/1", "S1:E3")]
    [InlineData("4.11.", "S5:E12")]
    [InlineData(".7.", "E8")]
    public void ConvertsXmltvNsEpisodeNumbers(string ns, string expected) => Assert.Equal(expected, NtvClient.FromXmltvNs(ns));
}

public class ServerAddressTests
{
    [Theory]
    [InlineData("http", "192.168.1.20", "http://192.168.1.20:8096|http://192.168.1.20")]
    [InlineData("https", "192.168.1.20", "https://192.168.1.20:8920|https://192.168.1.20")]
    [InlineData("http", "192.168.1.20:9000", "http://192.168.1.20:9000")]
    [InlineData("https", "jellyfin.example.com", "https://jellyfin.example.com|https://jellyfin.example.com:8920")]
    [InlineData("http", "[fd00::5]", "http://[fd00::5]:8096|http://[fd00::5]")]
    [InlineData("http", "[fd00::5]:8100", "http://[fd00::5]:8100")]
    public void TypedAddressGetsTheChosenSchemeAndDefaultPort(string scheme, string input, string expected) =>
        Assert.Equal(expected, string.Join("|", JellyfinClient.CandidatesFor(scheme, input)));
}

public class SettingsTests
{
    private sealed class TempPlatform : IPlatformInfo, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "nettle-tests-" + Guid.NewGuid().ToString("N"));
        public string CacheDirectory => Path.Combine(DataDirectory, "cache");
        public string DeviceName => "test";
        public string PlatformName => "test";
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch { } }
    }

    [Fact]
    public void BrokenSettingsFile_IsBackedUpBeforeReset()
    {
        using var p = new TempPlatform();
        var store = new SettingsStore(p);
        File.WriteAllText(Path.Combine(p.DataDirectory, "settings.json"), "{ this is not json");
        store.Load();
        Assert.Equal("ocean", store.Current.Theme); // defaults (Deep Sea)
        var backups = Directory.GetFiles(p.DataDirectory, "settings.json.broken-*");
        Assert.Single(backups);
        Assert.Equal("{ this is not json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public async Task Settings_RoundTrip()
    {
        using var p = new TempPlatform();
        var store = new SettingsStore(p);
        store.Load();
        store.Current.Theme = "neon";
        store.Current.Playback.StillWatchingPrompt = false;
        store.Current.Playback.NetworkBufferSeconds = 45;
        store.Current.Window = new WindowPlacement { Width = 1200, Height = 800, Maximized = true };
        await store.SaveAsync();

        var again = new SettingsStore(p);
        again.Load();
        Assert.Equal("neon", again.Current.Theme);
        Assert.False(again.Current.Playback.StillWatchingPrompt);
        Assert.Equal(45, again.Current.Playback.NetworkBufferSeconds);
        Assert.True(again.Current.Window!.Maximized);
        Assert.Empty(Directory.GetFiles(p.DataDirectory, "*.broken-*"));
    }

    [Fact]
    public void UpdateSource_IsPrefilled_AndOldEmptyValueIsFilledIn()
    {
        Assert.Equal("https://github.com/devall96/Nettle", new AppSettings().UpdateSource);
        Assert.False(new AppSettings().AutoCheckUpdates); // still opt-in: nothing is contacted until the user checks

        using var p = new TempPlatform();
        var store = new SettingsStore(p);
        File.WriteAllText(Path.Combine(p.DataDirectory, "settings.json"), "{\"UpdateSource\":\"\"}");
        store.Load();
        Assert.Equal(AppSettings.OfficialUpdateSource, store.Current.UpdateSource);

        File.WriteAllText(Path.Combine(p.DataDirectory, "settings.json"), "{\"UpdateSource\":\"D:\\\\my-releases\"}");
        store.Load();
        Assert.Equal(@"D:\my-releases", store.Current.UpdateSource); // a user's own source is kept
    }

    [Fact]
    public void StillWatching_IsOnByDefault()
    {
        var s = new PlaybackSettings();
        Assert.True(s.StillWatchingPrompt);
        Assert.InRange(s.StillWatchingEpisodes, 1, 20);
        Assert.False(s.AudioPassthrough);
    }
}

public class JellyfinModelTests
{
    [Fact]
    public void ReadsTrickplayLayouts()
    {
        const string json = """
        { "Id": "abc", "Trickplay": { "src1": { "320": { "Width": 320, "Height": 180, "TileWidth": 10, "TileHeight": 10, "ThumbnailCount": 250, "Interval": 10000, "Bandwidth": 1 } } } }
        """;
        var item = JsonSerializer.Deserialize(json, JellyfinJson.Default.BaseItemDto)!;
        var info = item.Trickplay!["src1"]["320"];
        Assert.Equal(10, info.TileWidth);
        Assert.Equal(250, info.ThumbnailCount);
        Assert.Equal(10000, info.Interval);
    }

    [Fact]
    public void ProgressReport_CarriesChosenStreams()
    {
        var json = JsonSerializer.Serialize(new PlaybackProgressInfo { ItemId = "x", AudioStreamIndex = 2, SubtitleStreamIndex = -1 }, JellyfinJson.Default.PlaybackProgressInfo);
        Assert.Contains("\"AudioStreamIndex\":2", json);
        Assert.Contains("\"SubtitleStreamIndex\":-1", json);
        Assert.Contains("\"PlayMethod\":\"DirectPlay\"", json);
    }

    [Fact]
    public void TrickplayTileUrl_HasNoTranscodeAndCarriesSource()
    {
        var jf = new JellyfinClient(new HttpClient(), "test", "0");
        jf.Configure("http://server:8096", "device", "token", "user");
        var url = jf.TrickplayTileUrl("item", "src", 320, 3);
        Assert.StartsWith("http://server:8096/Videos/item/Trickplay/320/3.jpg?", url);
        Assert.Contains("mediaSourceId=src", url);
    }
}

public class SeerrTests
{
    [Fact]
    public void ReadsPerSeasonStatus()
    {
        const string json = """
        { "id": 1, "name": "Show", "seasons": [ { "seasonNumber": 1, "episodeCount": 10 } ],
          "mediaInfo": { "status": 4, "seasons": [ { "seasonNumber": 1, "status": 5 } ] } }
        """;
        var d = JsonSerializer.Deserialize(json, SeerrJson.Default.SeerrDetails)!;
        Assert.Equal(5, d.MediaInfo!.Seasons![0].Status);
        Assert.Equal(10, d.Seasons![0].EpisodeCount);
    }

    [Fact]
    public void RequestBody_SendsOnlyChosenSeasons()
    {
        var body = new SeerrCreateRequest { MediaType = "tv", MediaId = 7, Seasons = new() { 2, 3 } };
        var json = JsonSerializer.Serialize(body, SeerrJson.Default.SeerrCreateRequest);
        Assert.Contains("\"seasons\":[2,3]", json);
        Assert.Contains("\"mediaType\":\"tv\"", json);
    }
}

public class PrivacyTests
{
    /// <summary>No analytics / crash-reporting SDKs or hard-coded third-party endpoints in the app.</summary>
    [Fact]
    public void Source_HasNoTelemetry()
    {
        var root = NeverTranscodeTests.RepoRoot();
        var forbidden = new[] { "ApplicationInsights", "Sentry", "AppCenter", "GoogleAnalytics", "Mixpanel", "Segment.Analytics", "Bugsnag", "Raygun" };
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs") || f.EndsWith(".csproj")))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            foreach (var f in forbidden)
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(f)}\b")) hits.Add($"{Path.GetRelativePath(root, file)}: {f}");
        }
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }
}
