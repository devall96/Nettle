using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Slopfin.Core.Json;
using Slopfin.Core.Models;

namespace Slopfin.Core.Api;

/// <summary>
/// Lean Jellyfin REST client (10.9+ routes). Hand-written so we only pay for what we use.
/// Playback is ALWAYS direct: Slopfin never requests HLS/transcoded streams.
/// </summary>
public sealed class JellyfinClient
{
    public const string ClientName = "Slopfin";
    public const string ListFields = "PrimaryImageAspectRatio,Overview,Genres,MediaSourceCount";
    public const string DetailFields = "Overview,Genres,People,Studios,Taglines,MediaSources,MediaStreams,Chapters,OriginalTitle,PrimaryImageAspectRatio";

    private readonly HttpClient _http;
    private readonly string _deviceName;
    private readonly string _version;

    public string? BaseUrl { get; private set; }
    public string? Token { get; private set; }
    public string? UserId { get; private set; }
    public string DeviceId { get; private set; } = Guid.NewGuid().ToString("N");

    public JellyfinClient(HttpClient http, string deviceName, string version)
    {
        _http = http;
        _deviceName = deviceName;
        _version = version;
    }

    public void Configure(string baseUrl, string deviceId, string? token = null, string? userId = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        DeviceId = deviceId;
        Token = token;
        UserId = userId;
    }

    public void ClearSession()
    {
        Token = null;
        UserId = null;
    }

    private string AuthHeader()
    {
        static string Esc(string s) => s.Replace("\"", "");
        var sb = new StringBuilder("MediaBrowser ");
        sb.Append($"Client=\"{ClientName}\", Device=\"{Esc(_deviceName)}\", DeviceId=\"{DeviceId}\", Version=\"{_version}\"");
        if (!string.IsNullOrEmpty(Token)) sb.Append($", Token=\"{Token}\"");
        return sb.ToString();
    }

    private string Url(string path) => BaseUrl + path;

    private HttpRequestMessage Req(HttpMethod m, string path)
    {
        var r = new HttpRequestMessage(m, Url(path));
        r.Headers.TryAddWithoutValidation("Authorization", AuthHeader());
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return r;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using (req)
        {
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new ApiException("Can't reach the server. " + ex.Message, null, ex); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ApiException("The server took too long to respond.", null, ex); }

            using (resp)
            {
                await EnsureOk(resp).ConfigureAwait(false);
                await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return (await JsonSerializer.DeserializeAsync(s, type, ct).ConfigureAwait(false))!;
            }
        }
    }

    private async Task SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using (req)
        {
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new ApiException("Can't reach the server. " + ex.Message, null, ex); }
            using (resp) await EnsureOk(resp).ConfigureAwait(false);
        }
    }

    private static async Task EnsureOk(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        string msg = resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired or the credentials are wrong.",
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "Not found on the server.",
            _ => $"Server returned {(int)resp.StatusCode} {resp.ReasonPhrase}."
        };
        try
        {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body) && body.Length < 300 && !body.TrimStart().StartsWith('<')) msg += " " + body.Trim();
        }
        catch { /* ignore */ }
        throw new ApiException(msg, resp.StatusCode);
    }

    private static HttpContent Json<T>(T value, JsonTypeInfo<T> type) =>
        new StringContent(JsonSerializer.Serialize(value, type), Encoding.UTF8, "application/json");

    // ---------- Server / auth ----------

    /// <summary>Normalizes user input ("myserver", "192.168.1.5:8096", "https://jf.example.com/") and probes it.</summary>
    public async Task<(string url, PublicSystemInfo info)> ProbeServerAsync(string input, CancellationToken ct = default)
    {
        input = input.Trim().TrimEnd('/');
        var candidates = new List<string>();
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(input);
        }
        else
        {
            bool hasPort = Uri.TryCreate("http://" + input, UriKind.Absolute, out var u) && !u.IsDefaultPort;
            candidates.Add("https://" + input);
            candidates.Add("http://" + input);
            if (!hasPort)
            {
                candidates.Add("http://" + input + ":8096");
                candidates.Add("https://" + input + ":8920");
            }
        }

        ApiException? last = null;
        foreach (var c in candidates)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                var req = new HttpRequestMessage(HttpMethod.Get, c + "/System/Info/Public");
                var info = await SendAsync(req, JellyfinJson.Default.PublicSystemInfo, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(info.Id)) return (c, info);
            }
            catch (ApiException ex) { last = ex; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { last = new ApiException("Timed out connecting to " + c); }
            catch (JsonException) { last = new ApiException(c + " doesn't look like a Jellyfin server."); }
        }
        throw last ?? new ApiException("Couldn't find a Jellyfin server at that address.");
    }

    /// <summary>LAN auto-discovery (UDP 7359), same mechanism the official clients use.</summary>
    public static async Task<List<DiscoveredServer>> DiscoverAsync(TimeSpan window, CancellationToken ct = default)
    {
        var found = new Dictionary<string, DiscoveredServer>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var msg = Encoding.UTF8.GetBytes("who is JellyfinServer?");
            await udp.SendAsync(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, 7359)).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(window);
            while (!cts.IsCancellationRequested)
            {
                var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    var s = JsonSerializer.Deserialize(r.Buffer, JellyfinJson.Default.DiscoveredServer);
                    if (s?.Id != null && s.Address != null) found[s.Id] = s;
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        return found.Values.ToList();
    }

    public Task<List<UserDto>> GetPublicUsersAsync(CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Users/Public"), JellyfinJson.Default.ListUserDto, ct);

    public async Task<AuthenticationResult> AuthenticateAsync(string user, string password, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, "/Users/AuthenticateByName");
        req.Content = Json(new AuthenticateUserByName { Username = user, Pw = password }, JellyfinJson.Default.AuthenticateUserByName);
        var res = await SendAsync(req, JellyfinJson.Default.AuthenticationResult, ct).ConfigureAwait(false);
        Token = res.AccessToken;
        UserId = res.User?.Id;
        return res;
    }

    public async Task<bool> IsQuickConnectEnabledAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = Req(HttpMethod.Get, "/QuickConnect/Enabled");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode && (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim() == "true";
        }
        catch { return false; }
    }

    public Task<QuickConnectResult> QuickConnectInitiateAsync(CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Post, "/QuickConnect/Initiate"), JellyfinJson.Default.QuickConnectResult, ct);

    public Task<QuickConnectResult> QuickConnectStateAsync(string secret, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/QuickConnect/Connect" + new Query().Add("secret", secret)), JellyfinJson.Default.QuickConnectResult, ct);

    public async Task<AuthenticationResult> QuickConnectAuthenticateAsync(string secret, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, "/Users/AuthenticateWithQuickConnect");
        req.Content = Json(new QuickConnectDto { Secret = secret }, JellyfinJson.Default.QuickConnectDto);
        var res = await SendAsync(req, JellyfinJson.Default.AuthenticationResult, ct).ConfigureAwait(false);
        Token = res.AccessToken;
        UserId = res.User?.Id;
        return res;
    }

    public Task<UserDto> GetMeAsync(CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Users/Me"), JellyfinJson.Default.UserDto, ct);

    public async Task LogoutAsync()
    {
        try { await SendAsync(Req(HttpMethod.Post, "/Sessions/Logout"), CancellationToken.None).ConfigureAwait(false); }
        catch { /* best effort */ }
    }

    // ---------- Browsing ----------

    public Task<QueryResult<BaseItemDto>> GetViewsAsync(CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/UserViews" + new Query().Add("userId", UserId)), JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetResumeAsync(int limit = 16, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/UserItems/Resume" + new Query()
            .Add("userId", UserId).Add("limit", limit).Add("mediaTypes", "Video")
            .Add("fields", ListFields).Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("imageTypeLimit", 1)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetNextUpAsync(int limit = 16, string? seriesId = null, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Shows/NextUp" + new Query()
            .Add("userId", UserId).Add("limit", limit).Add("seriesId", seriesId).Add("fields", ListFields)
            .Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("imageTypeLimit", 1).Add("enableResumable", false)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<List<BaseItemDto>> GetLatestAsync(string? parentId, int limit = 20, string? includeTypes = null, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Items/Latest" + new Query()
            .Add("userId", UserId).Add("parentId", parentId).Add("limit", limit).Add("includeItemTypes", includeTypes)
            .Add("fields", ListFields).Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("imageTypeLimit", 1)),
            JellyfinJson.Default.ListBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetItemsAsync(ItemQuery q, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Items" + new Query()
            .Add("userId", UserId).Add("parentId", q.ParentId).Add("includeItemTypes", q.IncludeItemTypes)
            .Add("recursive", q.Recursive).Add("sortBy", q.SortBy).Add("sortOrder", q.SortOrder)
            .Add("startIndex", q.StartIndex).Add("limit", q.Limit).Add("filters", q.Filters)
            .Add("searchTerm", q.SearchTerm).Add("genres", q.Genres).Add("personIds", q.PersonIds)
            .Add("fields", q.Fields ?? ListFields).Add("imageTypeLimit", 1)
            .Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("enableTotalRecordCount", true)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<BaseItemDto> GetItemAsync(string id, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Items/{id}" + new Query().Add("userId", UserId).Add("fields", DetailFields)),
            JellyfinJson.Default.BaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetSeasonsAsync(string seriesId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Shows/{seriesId}/Seasons" + new Query().Add("userId", UserId).Add("fields", ListFields)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetEpisodesAsync(string seriesId, string? seasonId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Shows/{seriesId}/Episodes" + new Query()
            .Add("userId", UserId).Add("seasonId", seasonId).Add("fields", ListFields + ",MediaSources")),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    /// <summary>
    /// Just the episodes either side of <paramref name="episodeId"/> (plus itself), in series
    /// order. Much cheaper than listing the whole series.
    /// </summary>
    public Task<QueryResult<BaseItemDto>> GetAdjacentEpisodesAsync(string seriesId, string episodeId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Shows/{seriesId}/Episodes" + new Query()
            .Add("userId", UserId).Add("adjacentTo", episodeId).Add("fields", "PrimaryImageAspectRatio")
            .Add("enableImageTypes", "Primary,Thumb").Add("imageTypeLimit", 1)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    /// <summary>One episode of a series by season and episode number (no full series listing).</summary>
    public async Task<BaseItemDto?> FindEpisodeAsync(string seriesId, int season, int episode, CancellationToken ct = default)
    {
        var r = await SendAsync(Req(HttpMethod.Get, "/Items" + new Query()
            .Add("userId", UserId).Add("parentId", seriesId).Add("includeItemTypes", "Episode").Add("recursive", true)
            .Add("parentIndexNumber", season).Add("indexNumber", episode).Add("limit", 5)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct).ConfigureAwait(false);
        // Older servers ignore the number filters, so check the numbers ourselves.
        return r.Items.FirstOrDefault(e => e.ParentIndexNumber == season && e.IndexNumber == episode);
    }

    /// <summary>The item's trickplay (scrubbing thumbnail) layouts, or null when it has none.</summary>
    public async Task<Dictionary<string, Dictionary<string, TrickplayInfo>>?> GetTrickplayAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var item = await SendAsync(Req(HttpMethod.Get, $"/Items/{itemId}" + new Query().Add("userId", UserId).Add("fields", "Trickplay")),
                JellyfinJson.Default.BaseItemDto, ct).ConfigureAwait(false);
            return item.Trickplay;
        }
        catch (ApiException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>One sheet of trickplay thumbnails (a JPEG holding TileWidth × TileHeight thumbnails).</summary>
    public string TrickplayTileUrl(string itemId, string mediaSourceId, int width, int index) =>
        $"{BaseUrl}/Videos/{itemId}/Trickplay/{width}/{index}.jpg" + new Query().Add("mediaSourceId", mediaSourceId).Add("ApiKey", Token);

    public Task<QueryResult<BaseItemDto>> GetSimilarAsync(string id, int limit = 16, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Items/{id}/Similar" + new Query().Add("userId", UserId).Add("limit", limit).Add("fields", ListFields)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task SetPlayedAsync(string id, bool played) =>
        SendAsync(Req(played ? HttpMethod.Post : HttpMethod.Delete, $"/UserPlayedItems/{id}" + new Query().Add("userId", UserId)), CancellationToken.None);

    public Task SetFavoriteAsync(string id, bool fav) =>
        SendAsync(Req(fav ? HttpMethod.Post : HttpMethod.Delete, $"/UserFavoriteItems/{id}" + new Query().Add("userId", UserId)), CancellationToken.None);

    public Task<List<PluginInfo>> GetPluginsAsync(CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Plugins"), JellyfinJson.Default.ListPluginInfo, ct);

    // ---------- Playback (direct play only) ----------

    public async Task<PlaybackInfoResponse> GetPlaybackInfoAsync(string itemId, long? startTicks, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, $"/Items/{itemId}/PlaybackInfo" + new Query().Add("userId", UserId));
        req.Content = Json(new PlaybackInfoDto { UserId = UserId, StartTimeTicks = startTicks }, JellyfinJson.Default.PlaybackInfoDto);
        return await SendAsync(req, JellyfinJson.Default.PlaybackInfoResponse, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The original file, byte-for-byte (static=true). This is the only video URL Slopfin ever builds.
    /// </summary>
    public string DirectStreamUrl(string itemId, string? mediaSourceId, string? playSessionId) =>
        $"{BaseUrl}/Videos/{itemId}/stream" + new Query()
            .Add("static", true).Add("mediaSourceId", mediaSourceId).Add("playSessionId", playSessionId)
            .Add("deviceId", DeviceId).Add("ApiKey", Token);

    public string ExternalSubtitleUrl(string itemId, string mediaSourceId, MediaStream s)
    {
        var codec = string.IsNullOrEmpty(s.Codec) ? "srt" : s.Codec.ToLowerInvariant() switch { "subrip" => "srt", "webvtt" => "vtt", var c => c };
        return $"{BaseUrl}/Videos/{itemId}/{mediaSourceId}/Subtitles/{s.Index}/0/Stream.{codec}" + new Query().Add("ApiKey", Token);
    }

    public Task ReportStartAsync(PlaybackProgressInfo p) => PostReport("/Sessions/Playing", p);
    public Task ReportProgressAsync(PlaybackProgressInfo p) => PostReport("/Sessions/Playing/Progress", p);
    public Task ReportStoppedAsync(PlaybackProgressInfo p) => PostReport("/Sessions/Playing/Stopped", p);

    private async Task PostReport(string path, PlaybackProgressInfo p)
    {
        try
        {
            var req = Req(HttpMethod.Post, path);
            req.Content = Json(p, JellyfinJson.Default.PlaybackProgressInfo);
            await SendAsync(req, CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* reporting must never break playback */ }
    }

    // ---------- Plugin-provided segments ----------

    /// <summary>Jellyfin 10.10+ Media Segments API (populated by Intro Skipper and other segment plugins).</summary>
    public async Task<List<MediaSegmentDto>> GetMediaSegmentsAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            var r = await SendAsync(Req(HttpMethod.Get, $"/MediaSegments/{itemId}"), JellyfinJson.Default.QueryResultMediaSegmentDto, ct).ConfigureAwait(false);
            return r.Items;
        }
        catch (ApiException) { return new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Intro Skipper plugin's own endpoint (works on servers before 10.10 too).</summary>
    public async Task<Dictionary<string, IntroSkipperSegment>> GetIntroSkipperSegmentsAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(Req(HttpMethod.Get, $"/Episode/{itemId}/IntroSkipperSegments"),
                JellyfinJson.Default.DictionaryStringIntroSkipperSegment, ct).ConfigureAwait(false) ?? new();
        }
        catch (ApiException) { return new(); }
        catch (JsonException) { return new(); }
    }

    // ---------- Images ----------

    public string? ImageUrl(string? itemId, string type, string? tag, int maxWidth, int quality = 90)
    {
        if (itemId == null || BaseUrl == null) return null;
        return $"{BaseUrl}/Items/{itemId}/Images/{type}" + new Query().Add("tag", tag).Add("maxWidth", maxWidth).Add("quality", quality);
    }

    public string? UserImageUrl(string? userId, string? tag, int maxWidth = 200, string? baseUrl = null)
    {
        if (userId == null || tag == null) return null;
        return $"{(baseUrl ?? BaseUrl)?.TrimEnd('/')}/Users/{userId}/Images/Primary" + new Query().Add("tag", tag).Add("maxWidth", maxWidth);
    }
}

public sealed class ItemQuery
{
    public string? ParentId { get; set; }
    public string? IncludeItemTypes { get; set; }
    public bool? Recursive { get; set; } = true;
    public string? SortBy { get; set; } = "SortName";
    public string? SortOrder { get; set; } = "Ascending";
    public int? StartIndex { get; set; }
    public int? Limit { get; set; }
    public string? Filters { get; set; }
    public string? SearchTerm { get; set; }
    public string? Genres { get; set; }
    public string? PersonIds { get; set; }
    public string? Fields { get; set; }
}
