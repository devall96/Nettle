using System.Text.Json;
using Nettle.Core.Json;
using Nettle.Core.Models;
using Nettle.Core.Services;

namespace Nettle.UI.Services;

/// <summary>
/// The last home screen per account (libraries, Continue Watching, Next Up, Recently added),
/// kept in the cache folder so the next start shows it instantly while fresh data loads.
/// Only titles and image references; removed when the account is signed out and forgotten.
/// </summary>
public static class HomeCache
{
    private static string Dir => Path.Combine(AppServices.Platform.CacheDirectory, "home");
    private static string PathFor(string accountId) => Path.Combine(Dir, accountId + ".json");

    public static HomeSnapshot? Load(string? accountId)
    {
        if (accountId == null) return null;
        try
        {
            var p = PathFor(accountId);
            if (!File.Exists(p)) return null;
            using var s = File.OpenRead(p);
            return JsonSerializer.Deserialize(s, JellyfinJson.Default.HomeSnapshot);
        }
        catch (Exception ex)
        {
            Log.Info("home cache unreadable, ignoring it: " + ex.Message);
            return null;
        }
    }

    public static void Save(string? accountId, HomeSnapshot snapshot)
    {
        if (accountId == null) return;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var p = PathFor(accountId);
                var tmp = p + ".tmp";
                File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(snapshot, JellyfinJson.Default.HomeSnapshot));
                File.Move(tmp, p, overwrite: true);
            }
            catch (Exception ex) { Log.Info("home cache not saved: " + ex.Message); }
        });
    }

    public static void Delete(string accountId)
    {
        try { File.Delete(PathFor(accountId)); } catch { }
    }
}
