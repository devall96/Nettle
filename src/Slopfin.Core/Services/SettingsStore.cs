using System.Text.Json;
using Slopfin.Core.Json;
using Slopfin.Core.Models;
using Slopfin.Core.Platform;

namespace Slopfin.Core.Services;

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
        catch
        {
            Current = new AppSettings();
        }
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
