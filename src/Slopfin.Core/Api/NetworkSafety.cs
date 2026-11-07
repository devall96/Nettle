using System.Net;
using System.Net.Sockets;

namespace Slopfin.Core.Api;

public static class NetworkSafety
{
    /// <summary>
    /// True when credentials would cross the internet unencrypted: plain http:// to a host that is
    /// not on the local network or a Tailscale tailnet (Tailscale traffic is WireGuard-encrypted).
    /// </summary>
    public static bool IsInsecureRemote(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme == Uri.UriSchemeHttps) return false;
        return !IsPrivateHost(u.Host);
    }

    public static bool IsPrivateHost(string host)
    {
        var h = host.ToLowerInvariant();
        if (h == "localhost" || h.EndsWith(".local") || h.EndsWith(".lan") || h.EndsWith(".home") ||
            h.EndsWith(".internal") || h.EndsWith(".ts.net") || !h.Contains('.'))
            return true;
        if (!IPAddress.TryParse(h, out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // CGNAT range used by Tailscale
    }
}
