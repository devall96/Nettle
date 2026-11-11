using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Nettle.Core.Json;
using Nettle.Core.Models;

namespace Nettle.Core.Api;

/// <summary>
/// Lean Jellyfin REST client (10.9+ routes). Hand-written so we only pay for what we use.
/// Playback is ALWAYS direct: Nettle never requests HLS/transcoded streams.
/// </summary>
public sealed class JellyfinClient
{
    public const string ClientName = "Nettle";
    /// <summary>For rows that feed the home banner (it shows the overview and genres).</summary>
    public const string ListFields = "PrimaryImageAspectRatio,Overview,Genres,MediaSourceCount";
    /// <summary>For cards (grids, rows, search): no overview / genre text, which cards never show.</summary>
    public const string CardFields = "PrimaryImageAspectRatio,MediaSourceCount";
    /// <summary>Episode lists show each episode's overview, but never need its media streams.</summary>
    public const string EpisodeFields = "PrimaryImageAspectRatio,Overview";
    public const string DetailFields = "Overview,Genres,People,Studios,Taglines,MediaSources,MediaStreams,OriginalTitle,PrimaryImageAspectRatio";

    /// <summary>
    /// Raised (on a background thread) when the server rejects the saved session with 401, i.e.
    /// the token was revoked or expired. Not raised for sign-in attempts.
    /// </summary>
    public event Action? SessionRejected;

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

    /// <summary>Everything <see cref="Configure"/> sets, to put it back after a failed switch.</summary>
    public (string? BaseUrl, string DeviceId, string? Token, string? UserId) Snapshot() => (BaseUrl, DeviceId, Token, UserId);

    public void Restore((string? BaseUrl, string DeviceId, string? Token, string? UserId) s)
    {
        BaseUrl = s.BaseUrl;
        DeviceId = s.DeviceId;
        Token = s.Token;
        UserId = s.UserId;
    }

    public void ClearSession()
    {
        Token = null;
        UserId = null;
    }

    private string AuthHeader() => AuthHeaderFor(DeviceId, Token);

    private string AuthHeaderFor(string deviceId, string? token)
    {
        static string Esc(string s) => s.Replace("\"", "");
        var sb = new StringBuilder("MediaBrowser ");
        sb.Append($"Client=\"{ClientName}\", Device=\"{Esc(_deviceName)}\", DeviceId=\"{deviceId}\", Version=\"{_version}\"");
        if (!string.IsNullOrEmpty(token)) sb.Append($", Token=\"{token}\"");
        return sb.ToString();
    }

    /// <summary>
    /// Ends another saved profile's session on its server (not the one signed in now), so a forgotten profile's
    /// token stops working there too. Best effort: an unreachable server just keeps it.
    /// </summary>
    public async Task RevokeAsync(string serverUrl, string deviceId, string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, serverUrl.TrimEnd('/') + "/Sessions/Logout");
            req.Headers.TryAddWithoutValidation("Authorization", AuthHeaderFor(deviceId, token));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// "Authorization" header value that carries only the token (no commas, so it also fits
    /// mpv's header list). Lets media URLs stay token-free, so the token never lands in
    /// reverse-proxy access logs. Null when signed out.
    /// </summary>
    public string? TokenHeaderValue => string.IsNullOrEmpty(Token) ? null : $"MediaBrowser Token=\"{Token}\"";

    /// <summary>True when <paramref name="url"/> points at the signed-in Jellyfin server.</summary>
    public bool IsServerUrl(string? url) =>
        BaseUrl != null && url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var b)
        && string.Equals(u.Host, b.Host, StringComparison.OrdinalIgnoreCase) && u.Port == b.Port && u.Scheme == b.Scheme;

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
                CheckRejected(req, resp);
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
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ApiException("The server took too long to respond.", null, ex); }
            using (resp)
            {
                CheckRejected(req, resp);
                await EnsureOk(resp).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A 401 on a request made with our token means the session is gone (not a wrong password).</summary>
    private void CheckRejected(HttpRequestMessage req, HttpResponseMessage resp)
    {
        if (resp.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(Token)) return;
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/Authenticate", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/Sessions/Logout", StringComparison.OrdinalIgnoreCase)) return;
        try { SessionRejected?.Invoke(); } catch { }
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

    /// <summary>
    /// Normalizes user input ("myserver", "192.168.1.5:8096", "https://jf.example.com/") and probes it.
    /// With <paramref name="scheme"/> ("http" / "https") and no scheme typed, only that scheme is tried,
    /// with Jellyfin's default port for it (8096 / 8920) unless a port was typed.
    /// </summary>
    public async Task<(string url, PublicSystemInfo info)> ProbeServerAsync(string input, CancellationToken ct = default, string? scheme = null)
    {
        input = input.Trim().TrimEnd('/');
        var candidates = new List<string>();
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(input);
        }
        else if (scheme is "http" or "https")
        {
            candidates.AddRange(CandidatesFor(scheme, input));
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

        // All candidates are tried at once (worst case is one timeout, not one per address), but
        // the answer is taken in preference order, so https still wins over http when both work.
        using var all = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var attempts = candidates.Select(c => ProbeOneAsync(c, all.Token)).ToList();
        ApiException? last = null;
        try
        {
            foreach (var attempt in attempts)
            {
                var (url, info, error) = await attempt.ConfigureAwait(false);
                if (info != null) return (url, info);
                last = error ?? last;
            }
        }
        finally
        {
            all.Cancel();
            // Let the rest finish quietly (each one catches its own errors).
            try { await Task.WhenAll(attempts).ConfigureAwait(false); } catch { }
        }
        ct.ThrowIfCancellationRequested();
        throw last ?? new ApiException("Couldn't find a Jellyfin server at that address.");
    }

    private async Task<(string Url, PublicSystemInfo? Info, ApiException? Error)> ProbeOneAsync(string c, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            var req = new HttpRequestMessage(HttpMethod.Get, c + "/System/Info/Public");
            var info = await SendAsync(req, JellyfinJson.Default.PublicSystemInfo, cts.Token).ConfigureAwait(false);
            return !string.IsNullOrEmpty(info.Id) ? (c, info, null) : (c, null, new ApiException(c + " doesn't look like a Jellyfin server."));
        }
        catch (ApiException ex) { return (c, null, ex); }
        catch (OperationCanceledException) { return (c, null, ct.IsCancellationRequested ? null : new ApiException("Timed out connecting to " + c)); }
        catch (JsonException) { return (c, null, new ApiException(c + " doesn't look like a Jellyfin server.")); }
        catch (Exception ex) { return (c, null, new ApiException("Can't reach " + c + ". " + ex.Message, null, ex)); }
    }

    /// <summary>
    /// Addresses to try for a host typed without a scheme. No port typed: an IP address most likely
    /// runs Jellyfin on its default port; a name (e.g. behind a reverse proxy) most likely on 80 / 443.
    /// </summary>
    internal static IEnumerable<string> CandidatesFor(string scheme, string input)
    {
        var bare = scheme + "://" + input;
        if (!Uri.TryCreate(bare, UriKind.Absolute, out var u)) { yield return bare; yield break; }
        bool hasPort = input.Contains("]:") || (!input.StartsWith('[') && input.Contains(':'));
        if (hasPort) { yield return bare; yield break; }
        var withPort = bare + (scheme == "https" ? ":8920" : ":8096");
        bool isIp = u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6;
        if (isIp) { yield return withPort; yield return bare; }
        else { yield return bare; yield return withPort; }
    }

    /// <summary>LAN auto-discovery (UDP 7359), same mechanism the official clients use.</summary>
    public static async Task<List<DiscoveredServer>> DiscoverAsync(TimeSpan window, CancellationToken ct = default)
    {
        var found = new Dictionary<string, DiscoveredServer>();
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            var msg = Encoding.UTF8.GetBytes("who is JellyfinServer?");
            // 255.255.255.255 only goes out of one network adapter (with a VPN such as Tailscale or Mullvad that is
            // often the VPN), so also ask each local network directly at its own broadcast address.
            foreach (var target in BroadcastAddresses())
            {
                try { await udp.SendAsync(msg, msg.Length, new IPEndPoint(target, 7359)).ConfigureAwait(false); }
                catch (SocketException) { /* that network can't be reached right now */ }
            }
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

    /// <summary>The general broadcast address plus each active IPv4 network's own (e.g. 192.168.1.255).</summary>
    internal static List<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var b = DirectedBroadcast(u.Address, u.IPv4Mask);
                    if (b != null && !list.Contains(b)) list.Add(b);
                }
            }
        }
        catch { /* the general broadcast alone */ }
        return list;
    }

    /// <summary>192.168.1.20 / 255.255.255.0 → 192.168.1.255; null for point-to-point links (/31, /32) such as VPN tunnels.</summary>
    internal static IPAddress? DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (a.Length != 4 || m.Length != 4) return null;
        uint mm = (uint)(m[0] << 24 | m[1] << 16 | m[2] << 8 | m[3]);
        if (mm == 0 || ~mm <= 1) return null;
        var b = new byte[4];
        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
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
            .Add("fields", CardFields).Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("imageTypeLimit", 1)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetNextUpAsync(int limit = 16, string? seriesId = null, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/Shows/NextUp" + new Query()
            .Add("userId", UserId).Add("limit", limit).Add("seriesId", seriesId).Add("fields", CardFields)
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
            .Add("ids", q.Ids).Add("years", q.Years).Add("is4K", q.Is4K).Add("isHd", q.IsHd).Add("nameLessThan", q.NameLessThan)
            .Add("isMissing", q.IsMissing)
            .Add("fields", q.Fields ?? CardFields).Add("imageTypeLimit", 1)
            .Add("enableImageTypes", "Primary,Backdrop,Thumb,Logo").Add("enableTotalRecordCount", q.EnableTotalRecordCount)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<BaseItemDto> GetItemAsync(string id, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Items/{id}" + new Query().Add("userId", UserId).Add("fields", DetailFields)),
            JellyfinJson.Default.BaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetSeasonsAsync(string seriesId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Shows/{seriesId}/Seasons" + new Query().Add("userId", UserId).Add("fields", CardFields)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct);

    public Task<QueryResult<BaseItemDto>> GetEpisodesAsync(string seriesId, string? seasonId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Shows/{seriesId}/Episodes" + new Query()
            .Add("userId", UserId).Add("seasonId", seasonId).Add("fields", EpisodeFields)),
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

    /// <summary>
    /// One sheet of trickplay thumbnails (a JPEG holding TileWidth × TileHeight thumbnails).
    /// No token in the URL: the image cache sends <see cref="TokenHeaderValue"/> to this server.
    /// </summary>
    public string TrickplayTileUrl(string itemId, string mediaSourceId, int width, int index) =>
        $"{BaseUrl}/Videos/{itemId}/Trickplay/{width}/{index}.jpg" + new Query().Add("mediaSourceId", mediaSourceId);

    /// <summary>Trailers stored next to the item (local files only; online trailers are never used).</summary>
    public async Task<List<BaseItemDto>> GetLocalTrailersAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(Req(HttpMethod.Get, $"/Items/{id}/LocalTrailers" + new Query().Add("userId", UserId)),
                JellyfinJson.Default.ListBaseItemDto, ct).ConfigureAwait(false);
        }
        catch (ApiException) { return new(); }
    }

    /// <summary>Extras (behind the scenes, deleted scenes, featurettes…) stored with the item.</summary>
    public async Task<List<BaseItemDto>> GetSpecialFeaturesAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(Req(HttpMethod.Get, $"/Items/{id}/SpecialFeatures" + new Query().Add("userId", UserId)),
                JellyfinJson.Default.ListBaseItemDto, ct).ConfigureAwait(false);
        }
        catch (ApiException) { return new(); }
    }

    /// <summary>Genres used in a library (names only).</summary>
    public async Task<List<string>> GetGenresAsync(string? parentId, string? includeTypes, CancellationToken ct = default)
    {
        var r = await SendAsync(Req(HttpMethod.Get, "/Genres" + new Query()
            .Add("userId", UserId).Add("parentId", parentId).Add("includeItemTypes", includeTypes).Add("sortBy", "SortName")
            .Add("enableImages", false).Add("enableTotalRecordCount", false)),
            JellyfinJson.Default.QueryResultBaseItemDto, ct).ConfigureAwait(false);
        return r.Items.Select(g => g.Name).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList();
    }

    // ---------- Subtitles found by the server (its subtitle provider plugins do the searching) ----------

    public Task<List<RemoteSubtitleInfo>> SearchRemoteSubtitlesAsync(string itemId, string language, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Items/{itemId}/RemoteSearch/Subtitles/{Uri.EscapeDataString(language)}"),
            JellyfinJson.Default.ListRemoteSubtitleInfo, ct);

    public Task DownloadRemoteSubtitleAsync(string itemId, string subtitleId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Post, $"/Items/{itemId}/RemoteSearch/Subtitles/{Uri.EscapeDataString(subtitleId)}"), ct);

    // ---------- Session capabilities (remote control from other Jellyfin apps) ----------

    public async Task PostCapabilitiesAsync(ClientCapabilitiesDto caps, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, "/Sessions/Capabilities/Full");
        req.Content = Json(caps, JellyfinJson.Default.ClientCapabilitiesDto);
        await SendAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>ws(s):// address of the server's WebSocket, or null when not configured.</summary>
    public Uri? SocketUri()
    {
        if (BaseUrl == null || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var b)) return null;
        var ub = new UriBuilder(b) { Scheme = b.Scheme == "https" ? "wss" : "ws", Port = b.Port };
        ub.Path = ub.Path.TrimEnd('/') + "/socket";
        ub.Query = "deviceId=" + Uri.EscapeDataString(DeviceId);
        return ub.Uri;
    }

    /// <summary>The headers the WebSocket handshake needs (full client identity + token).</summary>
    public string AuthorizationHeaderValue => AuthHeader();

    public Task<QueryResult<BaseItemDto>> GetSimilarAsync(string id, int limit = 16, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/Items/{id}/Similar" + new Query().Add("userId", UserId).Add("limit", limit).Add("fields", CardFields)),
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
    /// The original file, byte-for-byte (static=true). This is the only video URL Nettle ever builds.
    /// It carries no token: the player sends <see cref="TokenHeaderValue"/> as a header instead.
    /// </summary>
    public string DirectStreamUrl(string itemId, string? mediaSourceId, string? playSessionId) =>
        $"{BaseUrl}/Videos/{itemId}/stream" + new Query()
            .Add("static", true).Add("mediaSourceId", mediaSourceId).Add("playSessionId", playSessionId)
            .Add("deviceId", DeviceId);

    /// <summary>An external subtitle file as stored on the server (no conversion). Token goes in a header.</summary>
    public string ExternalSubtitleUrl(string itemId, string mediaSourceId, MediaStream s)
    {
        var codec = string.IsNullOrEmpty(s.Codec) ? "srt" : s.Codec.ToLowerInvariant() switch { "subrip" => "srt", "webvtt" => "vtt", var c => c };
        return $"{BaseUrl}/Videos/{itemId}/{mediaSourceId}/Subtitles/{s.Index}/0/Stream.{codec}";
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

    // ---------- SyncPlay (watch together) ----------
    // Bodies are built with JsonObject (no source-generated types needed); replies are read as JSON documents.

    private async Task<JsonDocument> GetDocAsync(string path, CancellationToken ct)
    {
        using var req = Req(HttpMethod.Get, path);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new ApiException("Can't reach the server. " + ex.Message, null, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new ApiException("The server took too long to respond.", null, ex); }
        using (resp)
        {
            CheckRejected(req, resp);
            await EnsureOk(resp).ConfigureAwait(false);
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    private Task PostSyncPlay(string action, System.Text.Json.Nodes.JsonObject? body = null, CancellationToken ct = default)
    {
        var req = Req(HttpMethod.Post, "/SyncPlay/" + action);
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return SendAsync(req, ct);
    }

    public async Task<List<SyncPlayGroupInfo>> SyncPlayListAsync(CancellationToken ct = default)
    {
        using var doc = await GetDocAsync("/SyncPlay/List", ct).ConfigureAwait(false);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(SyncPlayGroupInfo.From).Where(g => g != null).Select(g => g!).ToList()
            : new();
    }

    public async Task SyncPlayNewAsync(string name, CancellationToken ct = default) =>
        await PostSyncPlay("New", new() { ["GroupName"] = name }, ct).ConfigureAwait(false);

    public Task SyncPlayJoinAsync(string groupId, CancellationToken ct = default) => PostSyncPlay("Join", new() { ["GroupId"] = groupId }, ct);
    public Task SyncPlayLeaveAsync(CancellationToken ct = default) => PostSyncPlay("Leave", null, ct);
    public Task SyncPlayPauseAsync() => PostSyncPlay("Pause");
    public Task SyncPlayUnpauseAsync() => PostSyncPlay("Unpause");
    public Task SyncPlayStopAsync() => PostSyncPlay("Stop");
    public Task SyncPlaySeekAsync(long positionTicks) => PostSyncPlay("Seek", new() { ["PositionTicks"] = positionTicks });
    public Task SyncPlayNextItemAsync(string playlistItemId) => PostSyncPlay("NextItem", new() { ["PlaylistItemId"] = playlistItemId });
    public Task SyncPlayPreviousItemAsync(string playlistItemId) => PostSyncPlay("PreviousItem", new() { ["PlaylistItemId"] = playlistItemId });
    public Task SyncPlayPingAsync(long ms) => PostSyncPlay("Ping", new() { ["Ping"] = ms });

    /// <summary>The group plays these items (from <paramref name="index"/>), for everyone in it.</summary>
    public Task SyncPlaySetNewQueueAsync(IEnumerable<string> itemIds, int index, long startTicks)
    {
        var ids = new System.Text.Json.Nodes.JsonArray();
        foreach (var id in itemIds) ids.Add(id);
        return PostSyncPlay("SetNewQueue", new() { ["PlayingQueue"] = ids, ["PlayingItemPosition"] = index, ["StartPositionTicks"] = startTicks });
    }

    /// <summary>
    /// This player is ready (loaded, at <paramref name="positionTicks"/>) or still buffering. <paramref name="whenServer"/>
    /// is the server's clock: the group starts once everyone is ready.
    /// </summary>
    public Task SyncPlayReportAsync(bool ready, DateTime whenServer, long positionTicks, bool isPlaying, string playlistItemId) =>
        PostSyncPlay(ready ? "Ready" : "Buffering", new()
        {
            ["When"] = SyncPlayJson.Format(whenServer),
            ["PositionTicks"] = positionTicks,
            ["IsPlaying"] = isPlaying,
            ["PlaylistItemId"] = playlistItemId,
        });

    /// <summary>The server's clock: when it got the request and when it answered (both UTC).</summary>
    public async Task<(DateTime Received, DateTime Sent)> GetUtcTimeAsync(CancellationToken ct = default)
    {
        using var doc = await GetDocAsync("/GetUtcTime", ct).ConfigureAwait(false);
        var received = SyncPlayJson.Time(doc.RootElement, "RequestReceptionTime") ?? throw new ApiException("The server didn't send its time.");
        return (received, SyncPlayJson.Time(doc.RootElement, "ResponseTransmissionTime") ?? received);
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
        // WebP: about a third smaller than JPEG at the same quality, and keeps logos' transparency.
        return $"{BaseUrl}/Items/{itemId}/Images/{type}" + new Query().Add("tag", tag).Add("maxWidth", maxWidth).Add("quality", quality).Add("format", "Webp");
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
    /// <summary>Comma-separated item ids (e.g. what a remote control asked to play).</summary>
    public string? Ids { get; set; }
    /// <summary>Comma-separated years.</summary>
    public string? Years { get; set; }
    public bool? Is4K { get; set; }
    public bool? IsHd { get; set; }
    /// <summary>Only items whose sort name comes before this (used to count where a letter starts).</summary>
    public string? NameLessThan { get; set; }
    /// <summary>false = leave out placeholder episodes that have no file.</summary>
    public bool? IsMissing { get; set; }
    /// <summary>Counting every match costs the server a second query: later pages of a list skip it.</summary>
    public bool EnableTotalRecordCount { get; set; } = true;

    /// <summary>A copy with the same filters (for counting / paging the same result set).</summary>
    public ItemQuery Clone() => (ItemQuery)MemberwiseClone();
}
