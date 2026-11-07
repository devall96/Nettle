using Slopfin.UI;
using Velopack;
using Velopack.Sources;

namespace Slopfin.Desktop;

/// <summary>
/// Velopack-backed updater. Only runs when the user asks (Settings → Updates) and only talks to
/// the source they configured: a GitHub repo URL, a web folder, or a local/network folder.
/// </summary>
internal sealed class VelopackUpdater : IUpdater
{
    private UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public bool IsInstalled
    {
        get
        {
            try { return new UpdateManager(new SimpleFileSource(new DirectoryInfo(Path.GetTempPath()))).IsInstalled; }
            catch { return false; }
        }
    }

    public string CurrentVersion => AppServices.Version;

    private static UpdateManager Create(string source)
    {
        source = source.Trim();
        IUpdateSource src = source.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            ? new GithubSource(source.TrimEnd('/'), accessToken: null, prerelease: false)
            : source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new SimpleWebSource(source)
                : new SimpleFileSource(new DirectoryInfo(source));
        return new UpdateManager(src);
    }

    public async Task<string?> CheckAsync(string source, CancellationToken ct = default)
    {
        _mgr = Create(source);
        if (!_mgr.IsInstalled) throw new InvalidOperationException(OperatingSystem.IsWindows()
            ? "Updates work in the installed app (Slopfin Setup), not a copied folder."
            : "Updates work in the Slopfin AppImage, not an extracted or source build.");
        Slopfin.Core.Services.Log.Info("update check requested by user");
        _pending = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        if (_mgr == null || _pending == null) throw new InvalidOperationException("Check for updates first.");
        await _mgr.DownloadUpdatesAsync(_pending, p => progress?.Invoke(p)).ConfigureAwait(false);
        Slopfin.Core.Services.Log.Info($"applying update {_pending.TargetFullRelease.Version} and restarting");
        _mgr.ApplyUpdatesAndRestart(_pending);
    }
}
