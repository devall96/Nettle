using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Nettle.Core.Json;
using Nettle.Core.Models;

namespace Nettle.Core.Api;

/// <summary>
/// Seerr request-management client. Auth by API key (X-Api-Key) or by signing in with the
/// user's Jellyfin credentials, in which case only the resulting session cookie is persisted.
/// </summary>
public sealed class SeerrClient
{
    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;

    public string? BaseUrl { get; private set; }
    public string? ApiKey { get; private set; }
    public bool IsConfigured => BaseUrl != null && (ApiKey != null || SessionCookie != null);

    public string? SessionCookie
    {
        get
        {
            if (BaseUrl == null) return null;
            return _cookies.GetCookies(new Uri(BaseUrl))["connect.sid"]?.Value;
        }
    }

    public SeerrClient() => _http = HttpFactory.Create(TimeSpan.FromSeconds(20), _cookies);

    public void Configure(string? baseUrl, string? apiKey, string? sessionCookie)
    {
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : NormalizeUrl(baseUrl);
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (BaseUrl != null && !string.IsNullOrEmpty(sessionCookie))
            _cookies.Add(new Uri(BaseUrl), new Cookie("connect.sid", sessionCookie));
    }

    public static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "http://" + url;
        return url;
    }

    private HttpRequestMessage Req(HttpMethod m, string path)
    {
        var r = new HttpRequestMessage(m, BaseUrl + "/api/v1" + path);
        if (ApiKey != null) r.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return r;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, JsonTypeInfo<T> type, CancellationToken ct)
    {
        if (BaseUrl == null) throw new ApiException("Seerr isn't set up yet. Add it in Settings.");
        using (req)
        {
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new ApiException("Can't reach Seerr. " + ex.Message, null, ex); }
            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                    throw new ApiException(resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden
                        ? "Seerr rejected the credentials." : $"Seerr returned {(int)resp.StatusCode}.", resp.StatusCode);
                await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return (await JsonSerializer.DeserializeAsync(s, type, ct).ConfigureAwait(false))!;
            }
        }
    }

    public async Task<SeerrUser> SignInWithJellyfinAsync(string url, string username, string password, CancellationToken ct = default)
    {
        BaseUrl = NormalizeUrl(url);
        ApiKey = null;
        var req = Req(HttpMethod.Post, "/auth/jellyfin");
        req.Content = new StringContent(JsonSerializer.Serialize(new SeerrJellyfinLogin { Username = username, Password = password }, SeerrJson.Default.SeerrJellyfinLogin),
            Encoding.UTF8, "application/json");
        return await SendAsync(req, SeerrJson.Default.SeerrUser, ct).ConfigureAwait(false);
    }

    private SeerrUser? _me;
    private string? _meFor;

    /// <summary>The Seerr account Nettle is signed in as (cached per server + credentials).</summary>
    public async Task<SeerrUser> GetMeAsync(CancellationToken ct = default)
    {
        var key = BaseUrl + "|" + ApiKey + "|" + SessionCookie;
        if (_me != null && _meFor == key) return _me;
        var me = await SendAsync(Req(HttpMethod.Get, "/auth/me"), SeerrJson.Default.SeerrUser, ct).ConfigureAwait(false);
        _me = me;
        _meFor = key;
        return me;
    }

    public Task<SeerrPage> TrendingAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/discover/trending" + new Query().Add("page", page)), SeerrJson.Default.SeerrPage, ct);

    public Task<SeerrPage> PopularMoviesAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/discover/movies" + new Query().Add("page", page)), SeerrJson.Default.SeerrPage, ct);

    public Task<SeerrPage> PopularTvAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/discover/tv" + new Query().Add("page", page)), SeerrJson.Default.SeerrPage, ct);

    public Task<SeerrPage> UpcomingMoviesAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/discover/movies/upcoming" + new Query().Add("page", page)), SeerrJson.Default.SeerrPage, ct);

    public Task<SeerrPage> SearchAsync(string query, int page = 1, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, "/search" + new Query().Add("query", query).Add("page", page)), SeerrJson.Default.SeerrPage, ct);

    public Task<SeerrDetails> TvAsync(int tmdbId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/tv/{tmdbId}"), SeerrJson.Default.SeerrDetails, ct);

    public Task<SeerrDetails> MovieAsync(int tmdbId, CancellationToken ct = default) =>
        SendAsync(Req(HttpMethod.Get, $"/movie/{tmdbId}"), SeerrJson.Default.SeerrDetails, ct);

    /// <summary>
    /// Requests made by the signed-in Seerr user only. Seerr returns everyone's requests to
    /// admins, so the list is filtered by requester on the server and checked again here.
    /// </summary>
    public async Task<List<SeerrRequest>> MyRequestsAsync(int take = 30, CancellationToken ct = default)
    {
        var me = await GetMeAsync(ct).ConfigureAwait(false);
        var page = await SendAsync(Req(HttpMethod.Get, "/request" + new Query().Add("take", take).Add("skip", 0).Add("sort", "added").Add("requestedBy", me.Id)),
            SeerrJson.Default.SeerrRequestPage, ct).ConfigureAwait(false);
        return page.Results.Where(r => r.RequestedBy?.Id == me.Id).ToList();
    }

    /// <summary>
    /// Requests a movie, or the given seasons of a series (null = every season not already
    /// available or requested).
    /// </summary>
    public async Task<SeerrRequest> RequestAsync(SeerrResult item, IReadOnlyCollection<int>? seasons = null, CancellationToken ct = default)
    {
        var body = new SeerrCreateRequest { MediaType = item.MediaType ?? "movie", MediaId = item.Id };
        if (body.MediaType == "tv")
        {
            if (seasons is { Count: > 0 }) body.Seasons = seasons.ToList();
            else
            {
                var tv = await TvAsync(item.Id, ct).ConfigureAwait(false);
                var taken = tv.MediaInfo?.Seasons?.Where(s => s.Status >= SeerrMediaStatus.Pending).Select(s => s.SeasonNumber).ToHashSet() ?? new();
                body.Seasons = tv.Seasons?.Where(s => s.SeasonNumber > 0 && !taken.Contains(s.SeasonNumber)).Select(s => s.SeasonNumber).ToList() ?? new();
            }
        }
        var req = Req(HttpMethod.Post, "/request");
        req.Content = new StringContent(JsonSerializer.Serialize(body, SeerrJson.Default.SeerrCreateRequest), Encoding.UTF8, "application/json");
        return await SendAsync(req, SeerrJson.Default.SeerrRequest, ct).ConfigureAwait(false);
    }

    /// <summary>When true, posters load straight from image.tmdb.org (a third party). Off by default.</summary>
    public bool AllowDirectTmdbImages { get; set; }

    /// <summary>
    /// Poster URL. By default it goes through the user's own Seerr server image proxy
    /// (/imageproxy/tmdb/...), so TMDB never sees this PC's address or what is being browsed.
    /// </summary>
    public string? ImageUrl(string? path, int width = 342)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (AllowDirectTmdbImages) return $"https://image.tmdb.org/t/p/w{width}{path}";
        return BaseUrl == null ? null : $"{BaseUrl}/imageproxy/tmdb/t/p/w{width}{path}";
    }
}
