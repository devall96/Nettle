using System.Net;
using System.Text;
using Nettle.Core.Api;
using Nettle.Core.Models;

namespace Nettle.Tests;

/// <summary>Fixes from the 0.5.1 audit (desktop side; the Android TV ones are in android/core/src/test).</summary>
public class AuditFixTests
{
    private static List<NtvChannel> ParseGuide(string xml, params string[] channelIds)
    {
        var channels = channelIds.Select(id => new NtvChannel { Id = id, Name = id, StreamUrl = "http://x/stream/" + id }).ToList();
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        NtvClient.ParseXmltv(s, channels, DateTimeOffset.MinValue.AddYears(1), DateTimeOffset.MaxValue.AddYears(-1));
        return channels;
    }

    [Fact]
    public void Guide_ReadsEveryFieldOfAProgramme()
    {
        var ch = ParseGuide("""
            <tv>
              <programme start="20261110200000 +0000" stop="20261110203000 +0000" channel="a">
                <title>Show</title>
                <sub-title>The Pilot</sub-title>
                <desc>About it.</desc>
                <category>Comedy</category>
                <episode-num system="onscreen">S1E2</episode-num>
              </programme>
            </tv>
            """, "a");
        var p = Assert.Single(ch[0].Programs);
        Assert.Equal("Show", p.Title);
        Assert.Equal("The Pilot", p.SubTitle); // used to be skipped (every second element was)
        Assert.Equal("About it.", p.Description);
        Assert.Equal("Comedy", p.Category);
        Assert.Equal("S1E2", p.EpisodeNum);
    }

    [Fact]
    public void Guide_KeepsTheProgrammeAfterASkippedOne()
    {
        // "b" isn't a channel in the list, so its programme is skipped: the next one must still be read.
        var ch = ParseGuide("""
            <tv><programme start="20261110200000 +0000" stop="20261110203000 +0000" channel="b"><title>Elsewhere</title></programme><programme start="20261110203000 +0000" stop="20261110210000 +0000" channel="a"><title>Kept</title></programme><programme start="20261110210000 +0000" stop="20261110213000 +0000" channel="a"><title>Also kept</title></programme></tv>
            """, "a");
        Assert.Equal(new[] { "Kept", "Also kept" }, ch[0].Programs.Select(p => p.Title));
    }

    [Theory]
    [InlineData("#EXTINF:-1 tvg-id=\"a\",News, Weather & Sport", "News, Weather & Sport")]
    [InlineData("#EXTINF:-1 tvg-id=\"a\" group-title=\"Kids, Family\",Cartoons", "Cartoons")]
    [InlineData("#EXTINF:-1,Plain", "Plain")]
    [InlineData("#EXTINF:-1 tvg-id=\"a\"", null)]
    public void ChannelNames_MayContainCommas(string line, string? expected) => Assert.Equal(expected, NtvClient.ExtinfTitle(line));

    [Theory]
    [InlineData("http://[2001:db8::1]:8096", true)]   // public IPv6 over plain http: warn
    [InlineData("http://[fd7a:115c:a1e0::1]:8096", false)] // Tailscale / unique local
    [InlineData("http://[fe80::1]:8096", false)]
    [InlineData("http://[::1]:8096", false)]
    [InlineData("http://192.168.1.5:8096", false)]
    [InlineData("http://203.0.113.5:8096", true)]
    [InlineData("http://myserver:8096", false)]
    [InlineData("https://[2001:db8::1]", false)]
    public void PlainHttp_ToAPublicAddress_IsFlagged(string url, bool insecure) => Assert.Equal(insecure, NetworkSafety.IsInsecureRemote(url));

    [Theory]
    [InlineData("German - DTS - 5.1", "DTS", "DTS")]                  // "ma" in "German" is not DTS-HD MA
    [InlineData("English - DTS-HD MA - 7.1", "DTS-HD MA", "DTS-HD MA")]
    [InlineData("English - DTS-HD HRA - 5.1", "DTS-HD HRA", "DTS-HD")]
    public void DtsLabels_ComeFromTheRealProfile(string title, string profile, string expected) =>
        Assert.Equal(expected, VideoFormat.AudioLabel(new MediaStream { Type = "Audio", Codec = "dts", DisplayTitle = title, Profile = profile }));

    [Theory]
    [InlineData("DOVIWithEL", "Dolby Vision")]
    [InlineData("DOVIWithELHDR10Plus", "Dolby Vision")]
    [InlineData("DOVIWithHDR10", "Dolby Vision")]
    [InlineData("DOVIInvalid", null)]
    [InlineData("HDR10Plus", "HDR10+")]
    public void HdrLabels_CoverNewerDolbyVisionTypes(string range, string? expected) => Assert.Equal(expected, VideoFormat.HdrLabel(range, out _));

    [Theory]
    [InlineData("192.168.1.20", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.5.9", "255.255.0.0", "10.0.255.255")]
    [InlineData("10.0.0.7", "255.255.255.255", null)] // a single-address link (VPN tunnels look like this): no broadcast
    public void Discovery_AsksEachLocalNetwork(string address, string mask, string? expected) =>
        Assert.Equal(expected, JellyfinClient.DirectedBroadcast(IPAddress.Parse(address), IPAddress.Parse(mask))?.ToString());

    [Fact]
    public void SeerrSignIns_ArePerProfile()
    {
        Assert.Equal("seerr.cookie.abc", SeerrClient.CookieSecret("abc"));
        Assert.Equal("seerr.apikey.abc", SeerrClient.ApiKeySecret("abc"));
        Assert.NotEqual(SeerrClient.CookieSecret("abc"), SeerrClient.CookieSecret("def"));
        Assert.Equal(SeerrClient.LegacyCookieSecret, SeerrClient.CookieSecret(null));
    }

    [Fact]
    public void SeriesTrackChoices_SurviveTheSettingsFile()
    {
        var s = new AppSettings();
        s.Playback.SeriesTracks["series1"] = new SeriesTrackChoice { AudioLanguage = "jpn", SubtitleLanguage = "eng", LastUsed = 42 };
        var json = System.Text.Json.JsonSerializer.Serialize(s, Nettle.Core.Json.LocalJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, Nettle.Core.Json.LocalJson.Default.AppSettings)!;
        var c = back.Playback.SeriesTracks["series1"];
        Assert.Equal("jpn", c.AudioLanguage);
        Assert.Equal("eng", c.SubtitleLanguage);
        Assert.False(c.SubtitlesOff);
    }
}
