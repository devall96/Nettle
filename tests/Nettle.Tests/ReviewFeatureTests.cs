using System.Text.Json;
using Nettle.Core.Api;
using Nettle.Core.Json;
using Nettle.Core.Models;

namespace Nettle.Tests;

/// <summary>Media URLs, images, sessions and remote control added with the 0.4 review work.</summary>
public class TokenNeverInUrlTests
{
    private static JellyfinClient Client()
    {
        var jf = new JellyfinClient(new HttpClient(), "test", "0");
        jf.Configure("http://server:8096", "device", "secret-token-123456", "user");
        return jf;
    }

    [Fact]
    public void MediaUrls_CarryNoToken()
    {
        var jf = Client();
        var stream = jf.DirectStreamUrl("item", "source", "session");
        var sub = jf.ExternalSubtitleUrl("item", "source", new MediaStream { Index = 3, Codec = "subrip", Type = "Subtitle", IsExternal = true });
        var trick = jf.TrickplayTileUrl("item", "source", 320, 0);
        foreach (var url in new[] { stream, sub, trick })
        {
            Assert.DoesNotContain("secret-token", url);
            Assert.DoesNotContain("ApiKey", url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", url, StringComparison.OrdinalIgnoreCase);
        }
        Assert.EndsWith("/Subtitles/3/0/Stream.srt", sub);
    }

    [Fact]
    public void TokenHeader_HasNoCommas_AndOnlyMatchesOwnServer()
    {
        var jf = Client();
        // mpv splits its header list on commas, so the value must not contain any.
        Assert.Equal("MediaBrowser Token=\"secret-token-123456\"", jf.TokenHeaderValue);
        Assert.DoesNotContain(",", jf.TokenHeaderValue!);
        Assert.True(jf.IsServerUrl("http://server:8096/Videos/x/stream"));
        Assert.False(jf.IsServerUrl("http://server:19850/stream/1"));      // NostalgiaTV on the same machine
        Assert.False(jf.IsServerUrl("https://server:8096/Videos/x/stream")); // other scheme
        Assert.False(jf.IsServerUrl("http://elsewhere:8096/"));
    }

    [Fact]
    public void Images_AskForWebp()
    {
        var url = Client().ImageUrl("item", "Primary", "tag", 300)!;
        Assert.Contains("format=Webp", url);
        Assert.Contains("maxWidth=300", url);
    }

    [Fact]
    public void SocketUri_UsesWebSocketScheme_AndNoToken()
    {
        var jf = Client();
        var u = jf.SocketUri()!;
        Assert.Equal("ws", u.Scheme);
        Assert.Equal("/socket", u.AbsolutePath);
        Assert.DoesNotContain("secret-token", u.ToString());
        jf.Configure("https://jf.example.com/jellyfin", "device", "t", "u");
        var s = jf.SocketUri()!;
        Assert.Equal("wss", s.Scheme);
        Assert.Equal("/jellyfin/socket", s.AbsolutePath);
    }
}

public class SessionPreferenceTests
{
    [Fact]
    public void UserConfiguration_ReadsHiddenLatestLibraries()
    {
        const string json = """
        { "Id": "u1", "Name": "Sam", "Configuration": { "SubtitleMode": "OnlyForced", "AudioLanguagePreference": "jpn", "LatestItemsExcludes": ["lib1", "lib2"] } }
        """;
        var user = JsonSerializer.Deserialize(json, JellyfinJson.Default.UserDto)!;
        Assert.Equal("OnlyForced", user.Configuration!.SubtitleMode);
        Assert.Equal(new[] { "lib1", "lib2" }, user.Configuration.LatestItemsExcludes);
    }

    [Fact]
    public void HomeSnapshot_RoundTrips()
    {
        var snap = new HomeSnapshot
        {
            Views = [new BaseItemDto { Id = "v", Name = "Movies", CollectionType = "movies" }],
            Latest = { ["v"] = [new BaseItemDto { Id = "m", Name = "Film", Type = "Movie" }] },
        };
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(snap, JellyfinJson.Default.HomeSnapshot), JellyfinJson.Default.HomeSnapshot)!;
        Assert.Equal("Movies", back.Views[0].Name);
        Assert.Equal("Film", back.Latest["v"][0].Name);
    }

    [Fact]
    public void ItemQuery_Clone_IsIndependent()
    {
        var q = new ItemQuery { ParentId = "p", Genres = "Drama" };
        var c = q.Clone();
        c.NameLessThan = "M";
        Assert.Null(q.NameLessThan);
        Assert.Equal("Drama", c.Genres);
    }
}

public class ProbeTests
{
    [Fact]
    public void Probe_PrefersHttps_ButTriesAllAtOnce()
    {
        // Typed without a scheme: https first, then http, then the default ports.
        var c = JellyfinClient.CandidatesFor("https", "192.168.1.5").ToList();
        Assert.Equal("https://192.168.1.5:8920", c[0]);
    }
}
