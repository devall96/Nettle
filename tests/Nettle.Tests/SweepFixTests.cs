using Nettle.Core.Api;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.Tests;

/// <summary>Fixes from the full bug / performance sweep after 1.0.3.</summary>
public class SweepFixTests
{
    [Theory]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1920, 800, "1080p")]   // wide film: the width decides
    [InlineData(1440, 1080, "1080p")]  // 4:3 at 1080 lines: the height decides (was "720p")
    [InlineData(960, 720, "720p")]     // 4:3 at 720 lines (was "SD")
    [InlineData(3840, 1600, "4K")]
    [InlineData(720, 480, "SD")]
    public void Resolution_UsesHeightForNarrowPictures(int w, int h, string expected) =>
        Assert.Equal(expected, VideoFormat.Resolution(w, h));

    [Fact]
    public void Resolution_WidthAloneStillWorks()
    {
        Assert.Equal("1080p", VideoFormat.Resolution(1920));
        Assert.Null(VideoFormat.Resolution(null));
        Assert.Null(VideoFormat.Resolution(0, 0));
    }

    [Fact]
    public void XmltvTime_ImpossibleOffsetIsRejectedNotThrown() =>
        Assert.Null(NtvClient.ParseXmltvTime("20261110200000 +9900"));

    [Theory]
    [InlineData("X-Api-Key: abcd1234efgh5678")]
    [InlineData("X-API-Key=abcd1234efgh5678")]
    [InlineData("token=abcd+1234/efgh=5678")]
    public void Log_MasksHeaderStyleKeysAndBase64Values(string line)
    {
        var redacted = Log.Redact(line);
        Assert.DoesNotContain("1234", redacted);
        Assert.DoesNotContain("5678", redacted);
    }

    [Fact]
    public void Ntv_RemovingTheServerForgetsItsKey()
    {
        var ntv = new NtvClient();
        ntv.Configure("tv.lan", "secret-key");
        Assert.Equal("http://tv.lan:19850", ntv.BaseUrl);
        ntv.Configure(null, null);
        Assert.Null(ntv.BaseUrl);
        Assert.Null(ntv.ApiKey);
    }

    [Fact]
    public void Urls_AHostStartingWithHttpStillGetsAScheme()
    {
        Assert.Equal("http://httpbox.lan:5055", SeerrClient.NormalizeUrl("httpbox.lan:5055"));
        var ntv = new NtvClient();
        ntv.Configure("httpbox.lan:19850", null);
        Assert.Equal("http://httpbox.lan:19850", ntv.BaseUrl);
    }

    [Fact]
    public async Task Resolver_ASlotWithoutAJellyfinItemIsRefused()
    {
        var resolver = new NtvItemResolver(new JellyfinClient(HttpFactory.Create(), "Test", "1.0"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => resolver.GetPlaybackAsync(new NtvSlot { Title = "Weather", RatingKey = "plex:1:2" }));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
    }
}
