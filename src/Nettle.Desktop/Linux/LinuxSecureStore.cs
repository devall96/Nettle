using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Nettle.Core.Platform;

namespace Nettle.Desktop.Linux;

/// <summary>
/// Secrets on Linux go to the desktop keyring (GNOME Keyring, KWallet, KeePassXC... anything that
/// speaks the freedesktop Secret Service API), through libsecret. On systems with no keyring at
/// all, they fall back to files readable only by this user (mode 0600).
/// </summary>
internal sealed class LinuxSecureStore : ISecureStore
{
    private readonly string _fallbackDir;
    private readonly Lazy<SecretService?> _service;

    public LinuxSecureStore(string fallbackDir, bool useKeyring = true)
    {
        _fallbackDir = fallbackDir;
        _service = new(() => useKeyring ? SecretService.TryCreate() : null, isThreadSafe: true);
    }

    /// <summary>"Keyring" or "Files": where secrets are being kept, for diagnostics.</summary>
    public string Backend => _service.Value != null ? "keyring" : "files (0600)";

    public Task<string?> GetAsync(string key) => Task.Run(() =>
    {
        var svc = _service.Value;
        if (svc != null)
        {
            try
            {
                var v = svc.Lookup(key);
                if (v != null) return v;
            }
            catch (Exception ex) { Nettle.Core.Services.Log.Warn("keyring lookup failed: " + ex.Message); }
        }
        return ReadFile(key);
    });

    public Task SetAsync(string key, string value) => Task.Run(() =>
    {
        var svc = _service.Value;
        if (svc != null)
        {
            try
            {
                svc.Store(key, value);
                DeleteFile(key); // the keyring copy is now the only one
                return;
            }
            catch (Exception ex) { Nettle.Core.Services.Log.Warn("keyring store failed, using private file: " + ex.Message); }
        }
        WriteFile(key, value);
    });

    public Task RemoveAsync(string key) => Task.Run(() =>
    {
        try { _service.Value?.Clear(key); } catch (Exception ex) { Nettle.Core.Services.Log.Warn("keyring clear failed: " + ex.Message); }
        DeleteFile(key);
    });

    // ---------- 0600 file fallback ----------

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(_fallbackDir, hash + ".secret");
    }

    private string? ReadFile(string key)
    {
        var p = PathFor(key);
        try { return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null; }
        catch { return null; }
    }

    private void WriteFile(string key, string value)
    {
        Directory.CreateDirectory(_fallbackDir);
        try { File.SetUnixFileMode(_fallbackDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
        var p = PathFor(key);
        var tmp = p + ".tmp";
        // Create with 0600 from the start so the secret is never briefly world-readable.
        using (var fs = new FileStream(tmp, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        }))
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            fs.Write(bytes);
        }
        File.Move(tmp, p, overwrite: true);
    }

    private void DeleteFile(string key)
    {
        try { File.Delete(PathFor(key)); } catch { }
    }
}

/// <summary>Minimal libsecret binding: store / look up / clear one string per key.</summary>
internal sealed class SecretService
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGLib = "libglib-2.0.so.0";

    [DllImport(LibSecret)] private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection, IntPtr label, IntPtr password, IntPtr cancellable, out IntPtr error);
    [DllImport(LibSecret)] private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(LibSecret)] private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [DllImport(LibSecret)] private static extern void secret_password_free(IntPtr password);
    [DllImport(LibGLib)] private static extern IntPtr g_hash_table_new(IntPtr hashFunc, IntPtr equalFunc);
    [DllImport(LibGLib)] private static extern void g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);
    [DllImport(LibGLib)] private static extern void g_hash_table_unref(IntPtr table);
    [DllImport(LibGLib)] private static extern void g_error_free(IntPtr error);

    private readonly IntPtr _schema;
    private readonly IntPtr _strHash;
    private readonly IntPtr _strEqual;
    private static readonly IntPtr AttrName = Marshal.StringToCoTaskMemUTF8("key");

    private SecretService(IntPtr schema, IntPtr strHash, IntPtr strEqual)
    {
        _schema = schema;
        _strHash = strHash;
        _strEqual = strEqual;
    }

    /// <summary>Null when libsecret isn't installed or no Secret Service is running.</summary>
    public static SecretService? TryCreate()
    {
        try
        {
            if (!NativeLibrary.TryLoad(LibSecret, out _)) { Nettle.Core.Services.Log.Info("libsecret not found; secrets use private files"); return null; }
            if (!NativeLibrary.TryLoad(LibGLib, out var glib)) return null;
            var svc = new SecretService(BuildSchema(), NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
            // Probe: a lookup of a key that never exists talks to the service without prompting.
            svc.Lookup("nettle.probe");
            Nettle.Core.Services.Log.Info("secrets: using the desktop keyring");
            return svc;
        }
        catch (Exception ex)
        {
            Nettle.Core.Services.Log.Info("no desktop keyring available (" + ex.Message + "); secrets use private files");
            return null;
        }
    }

    /// <summary>
    /// SecretSchema (x86-64 layout): name ptr @0, flags @8, attributes[32] of {name ptr, type int} @16 (16 bytes each),
    /// then reserved fields. Total 592 bytes; zero-filled so the attribute list is NULL-terminated.
    /// </summary>
    private static IntPtr BuildSchema()
    {
        const int size = 592;
        var p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i += 8) Marshal.WriteInt64(p, i, 0);
        // Fixed internal ID from before the rename (Slopfin → Nettle): keyring entries are found by it.
        Marshal.WriteIntPtr(p, 0, Marshal.StringToCoTaskMemUTF8("io.github.devall96.Slopfin"));
        Marshal.WriteInt32(p, 8, 0);              // SECRET_SCHEMA_NONE
        Marshal.WriteIntPtr(p, 16, AttrName);     // attributes[0].name = "key"
        Marshal.WriteInt32(p, 24, 0);             // SECRET_SCHEMA_ATTRIBUTE_STRING
        return p; // lives for the whole process
    }

    private T WithAttributes<T>(string key, Func<IntPtr, T> call)
    {
        var table = g_hash_table_new(_strHash, _strEqual);
        var value = Marshal.StringToCoTaskMemUTF8(key);
        try
        {
            g_hash_table_insert(table, AttrName, value);
            return call(table);
        }
        finally
        {
            g_hash_table_unref(table);
            Marshal.FreeCoTaskMem(value);
        }
    }

    private static void ThrowIf(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        var msg = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown error";
        g_error_free(error);
        throw new InvalidOperationException(msg);
    }

    public string? Lookup(string key) => WithAttributes(key, attrs =>
    {
        var result = secret_password_lookupv_sync(_schema, attrs, IntPtr.Zero, out var error);
        ThrowIf(error);
        if (result == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(result); }
        finally { secret_password_free(result); } // libsecret wipes the memory
    });

    public void Store(string key, string value) => WithAttributes(key, attrs =>
    {
        var label = Marshal.StringToCoTaskMemUTF8("Nettle: " + key);
        var secret = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            secret_password_storev_sync(_schema, attrs, IntPtr.Zero, label, secret, IntPtr.Zero, out var error);
            ThrowIf(error);
            return 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(label);
            Marshal.ZeroFreeCoTaskMemUTF8(secret);
        }
    });

    public void Clear(string key) => WithAttributes(key, attrs =>
    {
        secret_password_clearv_sync(_schema, attrs, IntPtr.Zero, out var error);
        ThrowIf(error);
        return 0;
    });
}
