namespace Nettle.Core.Services;

/// <summary>
/// Tiny rolling diagnostic log (%LOCALAPPDATA%\Nettle\nettle.log). Thread-safe, best-effort,
/// capped at ~1 MB so it never grows unbounded. No secrets are ever written here.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "nettle.log");
        try
        {
            var fi = new FileInfo(_path);
            if (fi.Exists && fi.Length > 1_000_000) File.Move(_path, _path + ".old", overwrite: true);
            // (Older builds also re-read and scrubbed the whole log here on every start; every
            // line has been redacted as it's written for several releases, so that's gone.)
        }
        catch { }
        Info($"---- Nettle start ({Environment.OSVersion}, .NET {Environment.Version})");
    }

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERR", ex == null ? msg : $"{msg}: {ex}");

    private static readonly System.Text.RegularExpressions.Regex Secrets = new(
        "(?i)(api_?key|token|x-emby-token|access_?token)([=:\"\\s]+)[A-Za-z0-9._-]{8,}",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Masks access tokens / API keys anywhere in a message (including URLs from the player).</summary>
    public static string Redact(string? text) => string.IsNullOrEmpty(text) ? "" : Secrets.Replace(text, "$1$2***");

    /// <summary>Extra place to copy log lines to (already redacted), e.g. Android's logcat in test builds.</summary>
    public static Action<string, string>? Mirror { get; set; }

    private static void Write(string level, string msg)
    {
        if (_path == null) return;
        msg = Redact(msg);
        Mirror?.Invoke(level, msg);
        try
        {
            lock (Gate) File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
