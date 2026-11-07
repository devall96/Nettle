namespace Nettle.Core.Platform;

/// <summary>
/// Secret storage. Windows: DPAPI (per Windows user). Future heads: Android Keystore / iOS Keychain.
/// </summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}

/// <summary>Per-platform paths and identity.</summary>
public interface IPlatformInfo
{
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string DeviceName { get; }
    string PlatformName { get; }
}
