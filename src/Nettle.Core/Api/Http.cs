using System.Net;
using System.Text;

namespace Nettle.Core.Api;

public sealed class ApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public ApiException(string message, HttpStatusCode? status = null, Exception? inner = null) : base(message, inner) => StatusCode = status;
    public bool IsUnauthorized => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public static class HttpFactory
{
    /// <summary>One tuned, pooled handler for the whole app.</summary>
    public static HttpClient Create(TimeSpan? timeout = null, CookieContainer? cookies = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(8),
            MaxConnectionsPerServer = 12,
            UseCookies = cookies != null,
            CookieContainer = cookies ?? new CookieContainer(),
        };
        return new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
    }
}

/// <summary>Tiny query-string builder that skips nulls.</summary>
public sealed class Query
{
    private readonly StringBuilder _sb = new();

    public Query Add(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return this;
        _sb.Append(_sb.Length == 0 ? '?' : '&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
        return this;
    }

    public Query Add(string key, int? value) => value.HasValue ? Add(key, value.Value.ToString()) : this;
    public Query Add(string key, long? value) => value.HasValue ? Add(key, value.Value.ToString()) : this;
    public Query Add(string key, bool? value) => value.HasValue ? Add(key, value.Value ? "true" : "false") : this;

    public override string ToString() => _sb.ToString();
}
