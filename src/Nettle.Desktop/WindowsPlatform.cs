using System.Security.Cryptography;
using System.Text;
using Nettle.Core.Platform;

namespace Nettle.Desktop;

internal sealed class WindowsPlatformInfo : IPlatformInfo
{
    public string DataDirectory { get; } = Root();
    public string CacheDirectory { get; } = Path.Combine(Root(), "Cache");

    private static string Root()
    {
#if DEBUG
        // Developer convenience: a throwaway profile (e.g. for screenshots) that never touches real settings/accounts.
        var dev = Environment.GetEnvironmentVariable("NETTLE_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(dev)) return dev;
#endif
        return Path.Combine(LocalBase(), "Nettle");
    }

    private static string LocalBase()
    {
#if DEBUG
        // Tests of the Slopfin → Nettle data move against a copy, never the real folders.
        var dev = Environment.GetEnvironmentVariable("NETTLE_DEV_LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(dev)) return dev;
#endif
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>Brings over the data folder from the old name (see <see cref="LegacyData"/>). Run before anything uses the data folder.</summary>
    public static void MoveLegacyData()
    {
#if DEBUG
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NETTLE_PROFILE_DIR"))) return;
#endif
        LegacyData.MoveFolder(Path.Combine(LocalBase(), LegacyData.OldName), Root(), "data");
    }
    // Privacy: servers see a generic device name, never this PC's hostname.
    public string DeviceName { get; } = "Nettle (Windows)";
    public string PlatformName => "Windows";
}

/// <summary>
/// Secrets encrypted with Windows DPAPI (CurrentUser scope + app-specific entropy):
/// only this Windows account on this PC can decrypt them. Atomic writes, one file per key.
/// </summary>
internal sealed class DpapiSecureStore : ISecureStore
{
    // Fixed internal ID from before the rename (Slopfin → Nettle): changing it would make every
    // saved sign-in unreadable.
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
