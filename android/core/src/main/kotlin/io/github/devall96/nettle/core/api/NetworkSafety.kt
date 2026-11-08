package io.github.devall96.nettle.core.api

import java.net.Inet4Address
import java.net.Inet6Address
import java.net.InetAddress
import java.net.URI

object NetworkSafety {
    /**
     * True when credentials would cross the internet unencrypted: plain http:// to a host that is
     * not on the local network or a Tailscale tailnet (Tailscale traffic is WireGuard-encrypted).
     */
    fun isInsecureRemote(url: String?): Boolean {
        val u = runCatching { URI(url ?: return false) }.getOrNull() ?: return false
        if (u.host == null) return false
        if (u.scheme.equals("https", true)) return false
        return !isPrivateHost(u.host)
    }

    fun isPrivateHost(host: String): Boolean {
        val h = host.lowercase().trim('[', ']')
        if (h == "localhost" || h.endsWith(".local") || h.endsWith(".lan") || h.endsWith(".home") ||
            h.endsWith(".internal") || h.endsWith(".ts.net") || !h.contains('.') && !h.contains(':')
        ) return true
        val ip = parseIpLiteral(h) ?: return false
        if (ip.isLoopbackAddress) return true
        if (ip is Inet6Address) return ip.isLinkLocalAddress || ip.isSiteLocalAddress || (ip.address[0].toInt() and 0xFE) == 0xFC
        val b = ip.address.map { it.toInt() and 0xFF }
        return b[0] == 10 ||
            (b[0] == 172 && b[1] in 16..31) ||
            (b[0] == 192 && b[1] == 168) ||
            (b[0] == 169 && b[1] == 254) ||
            (b[0] == 100 && b[1] in 64..127) // CGNAT range used by Tailscale
    }

    /** Parses an IP literal without ever doing a DNS lookup (null for host names). */
    fun parseIpLiteral(h: String): InetAddress? {
        val v4 = Regex("""^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$""").matchEntire(h)
        if (v4 != null) {
            val parts = v4.groupValues.drop(1).map { it.toInt() }
            if (parts.any { it > 255 }) return null
            return Inet4Address.getByAddress(parts.map { it.toByte() }.toByteArray())
        }
        if (h.contains(':') && h.all { it.isLetterOrDigit() || it == ':' || it == '.' || it == '%' }) {
            return runCatching { InetAddress.getByName(h) }.getOrNull() // literal: no lookup
        }
        return null
    }
}
