using System.Text.Json;
using System.Text.RegularExpressions;
using Nettle.Core.Api;
using Nettle.Core.Json;
using Nettle.Core.Models;

namespace Nettle.Tests;

/// <summary>
/// Nettle's hardest rule: it never asks the server to transcode or remux. These tests fail the
/// build if anything starts requesting server-side conversion.
/// </summary>
public class NeverTranscodeTests
{
    [Fact]
    public void PlaybackInfoRequest_DisablesEveryKindOfServerConversion()
    {
        var dto = new PlaybackInfoDto();
        Assert.True(dto.EnableDirectPlay);
        Assert.False(dto.EnableDirectStream);
        Assert.False(dto.EnableTranscoding);
        Assert.False(dto.AllowVideoStreamCopy);
        Assert.False(dto.AllowAudioStreamCopy);
        Assert.Empty(dto.DeviceProfile.TranscodingProfiles);
    }

    [Fact]
    public void PlaybackInfoRequest_SerializesTheFlagsAsFalse()
    {
        var json = JsonSerializer.Serialize(new PlaybackInfoDto(), JellyfinJson.Default.PlaybackInfoDto);
        Assert.Contains("\"EnableTranscoding\":false", json);
        Assert.Contains("\"EnableDirectStream\":false", json);
        Assert.Contains("\"AllowVideoStreamCopy\":false", json);
        Assert.Contains("\"AllowAudioStreamCopy\":false", json);
        Assert.Contains("\"TranscodingProfiles\":[]", json);
    }

    [Fact]
    public void StreamUrl_IsTheOriginalFile()
    {
        var jf = new JellyfinClient(new HttpClient(), "test", "0");
        jf.Configure("http://server:8096", "device", "token", "user");
        var url = jf.DirectStreamUrl("item", "source", "session");
        Assert.StartsWith("http://server:8096/Videos/item/stream?", url);
        Assert.Contains("static=true", url);
        Assert.DoesNotContain("m3u8", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcod", url, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans all of Nettle's source for the ways a Jellyfin client asks for transcoded or
    /// remuxed streams. Any hit is a broken promise to users.
    /// </summary>
    [Fact]
    public void Source_NeverRequestsTranscodedStreams()
    {
        var root = RepoRoot();
        var forbidden = new (string Pattern, string Why)[]
        {
            (@"master\.m3u8|main\.m3u8|/hls1?/", "HLS (transcoding) playlist"),
            (@"TranscodingUrl", "server transcoding URL"),
            (@"EnableTranscoding\s*\{\s*get;\s*set;\s*\}\s*=\s*true|EnableTranscoding\s*=\s*true", "transcoding turned on"),
            (@"EnableDirectStream\s*\{\s*get;\s*set;\s*\}\s*=\s*true|EnableDirectStream\s*=\s*true", "remuxing turned on"),
            (@"AllowVideoStreamCopy\s*=\s*true|AllowAudioStreamCopy\s*=\s*true", "remuxing turned on"),
            (@"/Audio/[^""]*/universal", "universal (transcoding) audio endpoint"),
            (@"static""\s*,\s*false", "non-static (transcoded) stream"),
        };
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            foreach (var (pattern, why) in forbidden)
                foreach (Match m in Regex.Matches(text, pattern))
                    hits.Add($"{Path.GetRelativePath(root, file)}: {why} ('{m.Value}')");
        }
        Assert.True(hits.Count == 0, "Transcoding request found:\n" + string.Join("\n", hits));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Nettle.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repository root (Nettle.slnx).");
    }
}
