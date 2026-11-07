using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Slopfin.Core.Models;

namespace Slopfin.Core.Api;

/// <summary>
/// NostalgiaTV integration using the server's standard tuner outputs:
///   GET /channels.m3u  (channel list + stream URLs)
///   GET /xmltv.xml     (program guide)
/// Streams are MPEG-TS played as-is by mpv. Only live channels are ever exposed; the
/// NostalgiaTV on-demand section is deliberately not integrated.
/// </summary>
public sealed partial class NtvClient
{
    private readonly HttpClient _http;
    public string? BaseUrl { get; private set; }
    public string? ApiKey { get; private set; }
    public bool IsConfigured => BaseUrl != null;

    public NtvClient() => _http = HttpFactory.Create(TimeSpan.FromSeconds(60));

    public void Configure(string? baseUrl, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) { BaseUrl = null; return; }
        var u = baseUrl.Trim().TrimEnd('/');
        if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) u = "http://" + u;
        if (Uri.TryCreate(u, UriKind.Absolute, out var parsed) && parsed.IsDefaultPort && parsed.Scheme == "http" && u.IndexOf(':', 7) < 0)
            u += ":19850";
        BaseUrl = u;
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _guide = null; // different server or key: forget the cached guide
    }

    // The parsed guide is kept for a while: re-downloading and parsing several MB of XMLTV on
    // every visit to the Live TV page was the slowest part of opening it.
    private static readonly TimeSpan GuideMaxAge = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _guideGate = new(1, 1);
    private List<NtvChannel>? _guide;
    private DateTimeOffset _guideAt;
    private int _guideDays;

    /// <summary>When the cached guide was downloaded (null = nothing cached).</summary>
    public DateTimeOffset? GuideLoadedAt => _guide == null ? null : _guideAt;

    /// <summary>
    /// Channels plus the guide for [now - 3h, now + days]. Served from memory when a fresh
    /// enough copy covering as many days is cached, unless <paramref name="force"/>.
    /// </summary>
    public async Task<List<NtvChannel>> LoadGuideAsync(int days = 3, bool force = false, CancellationToken ct = default)
    {
        await _guideGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!force && _guide != null && _guideDays >= days && DateTimeOffset.Now - _guideAt < GuideMaxAge) return _guide;
            var g = await LoadGuideCoreAsync(days, ct).ConfigureAwait(false);
            _guide = g;
            _guideAt = DateTimeOffset.Now;
            _guideDays = days;
            return g;
        }
        finally { _guideGate.Release(); }
    }

    private async Task<Stream> GetStreamAsync(string path, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        if (ApiKey != null) req.Headers.TryAddWithoutValidation("X-API-Key", ApiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new ApiException("Can't reach NostalgiaTV. " + ex.Message, null, ex); }
        if (!resp.IsSuccessStatusCode)
        {
            resp.Dispose();
            throw new ApiException($"NostalgiaTV returned {(int)resp.StatusCode} for {path}. Is the HDHR/IPTV tuner enabled?", resp.StatusCode);
        }
        return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<NtvChannel>> LoadGuideCoreAsync(int days, CancellationToken ct)
    {
        if (BaseUrl == null) throw new ApiException("NostalgiaTV isn't set up yet. Add its address in Settings.");
        var m3uTask = GetStreamAsync("/channels.m3u", ct);
        var xmlTask = GetStreamAsync("/xmltv.xml", ct);

        List<NtvChannel> channels;
        await using (var m3u = await m3uTask.ConfigureAwait(false))
            channels = await ParseM3uAsync(m3u).ConfigureAwait(false);

        var now = DateTimeOffset.Now;
        await using (var xml = await xmlTask.ConfigureAwait(false))
            await Task.Run(() => ParseXmltv(xml, channels, now.AddHours(-3), now.AddDays(days)), ct).ConfigureAwait(false);

        foreach (var c in channels) c.Programs.Sort((a, b) => a.Start.CompareTo(b.Start));
        return channels;
    }

    /// <summary>
    /// The schedule rows for one channel (now → now + hours). Each row names the media-server
    /// item that airs in that slot, which is what enables original-quality direct play.
    /// </summary>
    public async Task<List<NtvSlot>> GetChannelScheduleAsync(string stableId, int hours = 6, CancellationToken ct = default)
    {
        if (BaseUrl == null) return new();
        await using var s = await GetStreamAsync($"/api/schedule?hours={hours}&channelId={Uri.EscapeDataString(stableId)}", ct).ConfigureAwait(false);
        var resp = await System.Text.Json.JsonSerializer.DeserializeAsync(s, Json.NtvJson.Default.NtvScheduleResponse, ct).ConfigureAwait(false);
        var ch = resp?.Channels.FirstOrDefault(c => string.Equals(c.StableId, stableId, StringComparison.OrdinalIgnoreCase))
                 ?? resp?.Channels.FirstOrDefault();
        var list = ch?.Programs ?? new();
        list.Sort((a, b) => a.StartTimeMs.CompareTo(b.StartTimeMs));
        return list;
    }

    [GeneratedRegex("([A-Za-z][\\w-]*)=\"([^\"]*)\"")]
    private static partial Regex AttrRegex();

    private async Task<List<NtvChannel>> ParseM3uAsync(Stream s)
    {
        var list = new List<NtvChannel>();
        using var reader = new StreamReader(s);
        string? line;
        Dictionary<string, string>? attrs = null;
        string? title = null;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                attrs = new(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in AttrRegex().Matches(line)) attrs[m.Groups[1].Value] = m.Groups[2].Value;
                var comma = line.LastIndexOf(',');
                title = comma >= 0 ? line[(comma + 1)..].Trim() : null;
            }
            else if (!line.StartsWith('#') && attrs != null)
            {
                var url = line.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? line : BaseUrl + "/" + line.TrimStart('/');
                // Security: only ever hand http(s) URLs to the player (never file://, smb://, etc.).
                if (!Uri.TryCreate(url, UriKind.Absolute, out var su) || (su.Scheme != Uri.UriSchemeHttp && su.Scheme != Uri.UriSchemeHttps))
                {
                    attrs = null;
                    title = null;
                    continue;
                }
                attrs.TryGetValue("tvg-id", out var id);
                attrs.TryGetValue("tvg-chno", out var chno);
                attrs.TryGetValue("channel-number", out var chno2);
                attrs.TryGetValue("tvg-logo", out var logo);
                attrs.TryGetValue("group-title", out var group);
                attrs.TryGetValue("tvg-name", out var tvgName);
                list.Add(new NtvChannel
                {
                    Id = string.IsNullOrEmpty(id) ? (chno ?? title ?? url) : id,
                    Name = title ?? tvgName ?? id ?? "Channel",
                    Number = chno ?? chno2,
                    LogoUrl = string.IsNullOrEmpty(logo) ? null : logo,
                    StreamUrl = url,
                    Group = group,
                });
                attrs = null;
                title = null;
            }
        }
        return list;
    }

    private static void ParseXmltv(Stream s, List<NtvChannel> channels, DateTimeOffset from, DateTimeOffset to)
    {
        var byId = new Dictionary<string, NtvChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in channels) byId.TryAdd(c.Id, c);

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, IgnoreComments = true, XmlResolver = null };
        using var r = XmlReader.Create(s, settings);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.Name == "channel")
            {
                var id = r.GetAttribute("id");
                if (id == null) continue;
                using var sub = r.ReadSubtree();
                string? icon = null;
                while (sub.Read())
                    if (sub.NodeType == XmlNodeType.Element && sub.Name == "icon") icon = sub.GetAttribute("src");
                if (byId.TryGetValue(id, out var ch) && ch.LogoUrl == null) ch.LogoUrl = icon;
            }
            else if (r.Name == "programme")
            {
                var chId = r.GetAttribute("channel");
                var start = ParseXmltvTime(r.GetAttribute("start"));
                var stop = ParseXmltvTime(r.GetAttribute("stop"));
                if (chId == null || start == null || stop == null || stop < from || start > to || !byId.TryGetValue(chId, out var ch))
                {
                    r.Skip();
                    continue;
                }
                string title = "", sub2 = "", desc = "", ep = "", icon = "", cat = "";
                using (var sub = r.ReadSubtree())
                {
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element) continue;
                        switch (sub.Name)
                        {
                            case "title": if (title.Length == 0) title = sub.ReadElementContentAsString(); break;
                            case "sub-title": sub2 = sub.ReadElementContentAsString(); break;
                            case "desc": desc = sub.ReadElementContentAsString(); break;
                            case "episode-num":
                                var sys = sub.GetAttribute("system");
                                var v = sub.ReadElementContentAsString();
                                if (sys == "xmltv_ns") v = FromXmltvNs(v) ?? "";
                                if (v.Length > 0 && (ep.Length == 0 || sys == "onscreen")) ep = v;
                                break;
                            case "icon": icon = sub.GetAttribute("src") ?? ""; break;
                            case "category": if (cat.Length == 0) cat = sub.ReadElementContentAsString(); break;
                        }
                    }
                }
                ch.Programs.Add(new NtvProgram
                {
                    Start = start.Value,
                    Stop = stop.Value,
                    Title = title,
                    SubTitle = sub2.Length > 0 ? sub2 : null,
                    Description = desc.Length > 0 ? desc : null,
                    EpisodeNum = ep.Length > 0 ? ep : null,
                    IconUrl = icon.Length > 0 ? icon : null,
                    Category = cat.Length > 0 ? cat : null,
                });
            }
        }
    }

    /// <summary>xmltv_ns is zero-based "season.episode.part" (e.g. "0.2.0/1") → "S1:E3".</summary>
    internal static string? FromXmltvNs(string v)
    {
        var parts = v.Split('.');
        if (parts.Length < 2) return null;
        static int? Num(string s)
        {
            var head = s.Split('/')[0].Trim();
            return int.TryParse(head, out var n) ? n + 1 : null;
        }
        var season = Num(parts[0]);
        var episode = Num(parts[1]);
        if (season != null && episode != null) return $"S{season}:E{episode}";
        if (episode != null) return $"E{episode}";
        return null;
    }

    /// <summary>XMLTV time:"yyyyMMddHHmmss +hhmm" (offset optional).</summary>
    internal static DateTimeOffset? ParseXmltvTime(string? v)
    {
        if (string.IsNullOrEmpty(v) || v.Length < 12) return null;
        var digits = v.Length >= 14 ? v[..14] : v[..12] + "00";
        if (!DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return null;
        var offset = TimeSpan.Zero;
        var rest = v.Length > 14 ? v[14..].Trim() : "";
        if (rest.Length >= 5 && (rest[0] == '+' || rest[0] == '-') &&
            int.TryParse(rest.AsSpan(1, 2), out var hh) && int.TryParse(rest.AsSpan(3, 2), out var mm))
        {
            offset = new TimeSpan(hh, mm, 0);
            if (rest[0] == '-') offset = -offset;
        }
        return new DateTimeOffset(dt, offset);
    }
}
