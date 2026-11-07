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
    private readonly ConcurrentDictionary<string, Inflight> _inflight = new();

    /// <summary>
    /// One download+decode shared by everyone asking for the same image. It is abandoned (before
    /// it uses a download slot, or mid-download) once every caller has cancelled, e.g. when the
    /// posters that wanted it have scrolled away.
    /// </summary>
    private sealed class Inflight
    {
        public readonly CancellationTokenSource Cts = new();
        public Task<Bitmap?> Task = null!;
        public int Waiters;
    }

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

    private bool Permitted(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (IsAllowed == null || IsAllowed(uri))) return true;
        if (uri != null && _blockedHosts.TryAdd(uri.Host, 0))
            Slopfin.Core.Services.Log.Info($"image blocked by privacy allowlist (host {uri.Host})");
        return false;
    }

    public async Task<Bitmap?> LoadAsync(string url, int width, CancellationToken ct = default)
    {
        if (!Permitted(url)) return null;
        var mem = TryGetMemory(url, width);
        if (mem != null) return mem;
        var key = Key(url, width);

        Inflight entry;
        while (true)
        {
            entry = _inflight.GetOrAdd(key, k =>
            {
                var e = new Inflight();
                e.Task = Task.Run(async () =>
                {
                    try { return await LoadCoreAsync(url, width, k, e.Cts.Token).ConfigureAwait(false); }
                    finally { _inflight.TryRemove(new KeyValuePair<string, Inflight>(k, e)); }
                });
                return e;
            });
            // An entry everyone already gave up on is on its way out: start a fresh one.
            Interlocked.Increment(ref entry.Waiters);
            if (!entry.Cts.IsCancellationRequested) break;
            _inflight.TryRemove(new KeyValuePair<string, Inflight>(key, entry));
        }

        using var reg = ct.Register(() =>
        {
            if (Interlocked.Decrement(ref entry.Waiters) == 0) entry.Cts.Cancel();
        });
        try { return await entry.Task.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>
    /// Downloads (or reads from disk) and decodes at full size without keeping it in the memory
    /// cache. For large one-off images such as trickplay sheets.
    /// </summary>
    public async Task<Bitmap?> LoadUncachedAsync(string url)
    {
        if (!Permitted(url)) return null;
        var bytes = await GetBytesAsync(url, CancellationToken.None).ConfigureAwait(false);
        if (bytes == null) return null;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    private async Task<byte[]?> GetBytesAsync(string url, CancellationToken ct)
    {
        var file = Path.Combine(_dir, Hash(StripToken(url)));
        try
        {
            if (File.Exists(file)) return await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
            byte[] bytes;
            await _downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            finally { _downloads.Release(); }
            try { await File.WriteAllBytesAsync(file, bytes, CancellationToken.None).ConfigureAwait(false); } catch { /* cache is best-effort */ }
            return bytes;
        }
        catch { return null; }
    }

    /// <summary>The disk cache is keyed without the access token, so a new sign-in still hits it.</summary>
    private static string StripToken(string url) =>
        System.Text.RegularExpressions.Regex.Replace(url, "(?i)([?&])(ApiKey|api_key)=[^&]*&?", "$1").TrimEnd('&', '?');

    private async Task<Bitmap?> LoadCoreAsync(string url, int width, string key, CancellationToken ct)
    {
        var file = Path.Combine(_dir, Hash(StripToken(url)));
        var bytes = await GetBytesAsync(url, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested) return null;

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
