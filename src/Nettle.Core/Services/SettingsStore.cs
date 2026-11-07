using System.Text.Json;
using Nettle.Core.Json;
using Nettle.Core.Models;
using Nettle.Core.Platform;

namespace Nettle.Core.Services;

/// <summary>Non-secret preferences (theme, playback options). Secrets never go here.</summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettings Current { get; private set; } = new();
    public event Action? Changed;

    public SettingsStore(IPlatformInfo platform)
    {
        Directory.CreateDirectory(platform.DataDirectory);
        _path = Path.Combine(platform.DataDirectory, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize(File.ReadAllText(_path), LocalJson.Default.AppSettings) ?? new();
        }
        catch (Exception ex)
        {
            // The file couldn't be read: start from defaults, but keep a copy of the old file
            // first, since the next save would otherwise overwrite it for good.
            BackupDamagedFile(_path, ex);
            Current = new AppSettings();
        }
    }

    /// <summary>Copies an unreadable file to "name.broken-yyyyMMdd-HHmmss" next to it.</summary>
    public static string? BackupDamagedFile(string path, Exception? reason = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup, overwrite: true);
            Log.Warn($"{Path.GetFileName(path)} couldn't be read ({reason?.Message}); reset to defaults, old copy kept as {Path.GetFileName(backup)}");
            return backup;
        }
        catch { return null; }
    }

    public async Task SaveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(Current, LocalJson.Default.AppSettings));
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }
}
