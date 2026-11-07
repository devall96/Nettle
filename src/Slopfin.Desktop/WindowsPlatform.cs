using System.Security.Cryptography;
using System.Text;
using Slopfin.Core.Platform;

namespace Slopfin.Desktop;

internal sealed class WindowsPlatformInfo : IPlatformInfo
{
    public string DataDirectory { get; } = Root();
    public string CacheDirectory { get; } = Path.Combine(Root(), "Cache");

    private static string Root()
    {
#if DEBUG
        // Developer convenience: a throwaway profile (e.g. for screenshots) that never touches real settings/accounts.
        var dev = Environment.GetEnvironmentVariable("SLOPFIN_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return dev;
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slopfin");
    }
    // Privacy: servers see a generic device name, never this PC's hostname.
    public string DeviceName { get; } = "Slopfin (Windows)";
    public string PlatformName => "Windows";
}

/// <summary>
/// Secrets encrypted with Windows DPAPI (CurrentUser scope + app-specific entropy):
/// only this Windows account on this PC can decrypt them. Atomic writes, one file per key.
/// </summary>
internal sealed class DpapiSecureStore : ISecureStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Slopfin.SecureStore.v1");
    private readonly string _dir;

    public DpapiSecureStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(_dir, hash + ".bin");
    }

    public async Task<string?> GetAsync(string key)
    {
        var p = PathFor(key);
        if (!File.Exists(p)) return null;
        try
        {
            var data = await File.ReadAllBytesAsync(p);
            var plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null; // created by another Windows user / machine
        }
    }

    public async Task SetAsync(string key, string value)
    {
        var p = PathFor(key);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        var tmp = p + ".tmp";
        await File.WriteAllBytesAsync(tmp, enc);
        File.Move(tmp, p, overwrite: true);
    }

    public Task RemoveAsync(string key)
    {
        try { File.Delete(PathFor(key)); } catch { }
        return Task.CompletedTask;
    }
}
