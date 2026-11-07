using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Slopfin.Core.Api;

namespace Slopfin.UI.Services;

/// <summary>
/// Two-level image cache tuned for smooth scrolling:
///  * memory LRU of decoded bitmaps (decoded at display size, never full-res),
///  * disk cache of downloaded bytes,
///  * in-flight request de-duplication and bounded concurrency.
/// </summary>
public sealed class ImageCache
{
    private const long MemoryBudgetBytes = 220L * 1024 * 1024;
    private const long DiskBudgetBytes = 600L * 1024 * 1024;

    private readonly string _dir;
    private readonly HttpClient _http = HttpFactory.Create(TimeSpan.FromSeconds(20));
    private readonly SemaphoreSlim _downloads = new(8);
    private readonly SemaphoreSlim _decodes = new(Math.Max(2, Environment.ProcessorCount / 2));
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new();

    private readonly object _lruLock = new();
    private readonly Dictionary<string, LinkedListNode<(string key, Bitmap bmp, long size)>> _map = new();
    private readonly LinkedList<(string key, Bitmap bmp, long size)> _lru = new();
    private long _memBytes;

    public ImageCache(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
        _ = Task.Run(TrimDisk);
    }

    private static string Key(string url, int width) => width + "|" + url;

    public Bitmap? TryGetMemory(string url, int width)
    {
        lock (_lruLock)
        {
            if (_map.TryGetValue(Key(url, width), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.bmp;
            }
        }
        return null;
    }

    /// <summary>
    /// Privacy allowlist: decides which hosts images may be fetched from. Anything else is never
    /// requested (e.g. a logo URL in a channel list pointing at some third-party site).
    /// </summary>
    public Func<Uri, bool>? IsAllowed { get; set; }
    private readonly ConcurrentDictionary<string, byte> _blockedHosts = new();

    public Task<Bitmap?> LoadAsync(string url, int width)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || (IsAllowed != null && !IsAllowed(uri)))
        {
            if (uri != null && _blockedHosts.TryAdd(uri.Host, 0))
                Slopfin.Core.Services.Log.Info($"image blocked by privacy allowlist (host {uri.Host})");
            return Task.FromResult<Bitmap?>(null);
        }
        var mem = TryGetMemory(url, width);
        if (mem != null) return Task.FromResult<Bitmap?>(mem);
        var key = Key(url, width);
        return _inflight.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try { return await LoadCoreAsync(url, width, key).ConfigureAwait(false); }
            finally { _inflight.TryRemove(key, out Task<Bitmap?>? _); }
        }));
    }

    private async Task<Bitmap?> LoadCoreAsync(string url, int width, string key)
    {
        byte[]? bytes = null;
        var file = Path.Combine(_dir, Hash(url));
        try
        {
            if (File.Exists(file))
            {
                bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            }
            else
            {
                await _downloads.WaitAsync().ConfigureAwait(false);
                try
                {
                    using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return null;
                    bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                finally { _downloads.Release(); }
                try { await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false); } catch { /* cache is best-effort */ }
            }
        }
        catch
        {
            return null;
        }

        if (bytes == null || bytes.Length == 0) return null;

        await _decodes.WaitAsync().ConfigureAwait(false);
        Bitmap bmp;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            bmp = width > 0 ? Bitmap.DecodeToWidth(ms, width, BitmapInterpolationMode.MediumQuality) : new Bitmap(ms);
        }
        catch
        {
            try { File.Delete(file); } catch { }
            return null;
        }
        finally { _decodes.Release(); }

        long size = (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;
        lock (_lruLock)
        {
            if (!_map.ContainsKey(key))
            {
                _map[key] = _lru.AddFirst((key, bmp, size));
                _memBytes += size;
                while (_memBytes > MemoryBudgetBytes && _lru.Last != null)
                {
                    var last = _lru.Last.Value;
                    _lru.RemoveLast();
                    _map.Remove(last.key);
                    _memBytes -= last.size;
                    // Not disposed: it may still be on screen. GC reclaims it once unreferenced.
                }
            }
        }
        return bmp;
    }

    private static string Hash(string s)
    {
        Span<byte> h = stackalloc byte[20];
        SHA1.HashData(Encoding.UTF8.GetBytes(s), h);
        return Convert.ToHexString(h);
    }

    private void TrimDisk()
    {
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles();
            long total = files.Sum(f => f.Length);
            if (total <= DiskBudgetBytes) return;
            foreach (var f in files.OrderBy(f => f.LastAccessTimeUtc))
            {
                total -= f.Length;
                f.Delete();
                if (total < DiskBudgetBytes * 0.7) break;
            }
        }
        catch { }
    }

    public void ClearDisk()
    {
        try { foreach (var f in Directory.GetFiles(_dir)) File.Delete(f); } catch { }
    }
}
