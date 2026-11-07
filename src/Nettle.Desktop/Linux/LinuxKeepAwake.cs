using Nettle.UI;
using Tmds.DBus.Protocol;

namespace Nettle.Desktop.Linux;

/// <summary>
/// Keeps the screen on during playback by asking the desktop over the session D-Bus. With the
/// render API mpv can't do this itself. Tries GNOME's session manager (flag 8 = "idle"), then the
/// freedesktop ScreenSaver interface (KDE, Xfce, Cinnamon, …). Both tie the inhibit to our D-Bus
/// connection, so it's released automatically if Nettle exits or crashes.
/// </summary>
internal sealed class LinuxKeepAwake : IKeepAwake
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Connection? _conn;
    private bool _want;
    private uint? _cookie;
    private bool _gnome;
    private bool _unsupported;

    public void SetActive(bool on)
    {
        _want = on;
        _ = ApplyAsync();
    }

    private async Task ApplyAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_unsupported) return;
            if (_want && _cookie == null) await InhibitAsync().ConfigureAwait(false);
            else if (!_want && _cookie is { } c)
            {
                _cookie = null;
                await CallAsync(_gnome ? Gnome : FreeDesktop, _gnome ? "Uninhibit" : "UnInhibit", "u", w => w.WriteUInt32(c)).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { Nettle.Core.Services.Log.Warn("keep-awake: " + ex.Message); }
        finally { _gate.Release(); }
    }

    private sealed record Target(string Service, string Path, string Interface);
    private static readonly Target Gnome = new("org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager");
    private static readonly Target FreeDesktop = new("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver", "org.freedesktop.ScreenSaver");

    private async Task InhibitAsync()
    {
        const string reason = "Playing video";
        try
        {
            _cookie = await CallAsync(Gnome, "Inhibit", "susu", w =>
            {
                w.WriteString("Nettle");
                w.WriteUInt32(0);
                w.WriteString(reason);
                w.WriteUInt32(8); // GSM_INHIBITOR_FLAG_IDLE
            }, read: true).ConfigureAwait(false);
            _gnome = true;
            Nettle.Core.Services.Log.Info("keep-awake: screen kept on (GNOME session manager)");
            return;
        }
        catch (DBusException) { }

        try
        {
            _cookie = await CallAsync(FreeDesktop, "Inhibit", "ss", w =>
            {
                w.WriteString("Nettle");
                w.WriteString(reason);
            }, read: true).ConfigureAwait(false);
            _gnome = false;
            Nettle.Core.Services.Log.Info("keep-awake: screen kept on (freedesktop ScreenSaver)");
        }
        catch (DBusException ex)
        {
            _unsupported = true; // no service that can do it: don't retry for every play/pause
            Nettle.Core.Services.Log.Warn("keep-awake: desktop offers no screensaver inhibit (" + ex.Message + ")");
        }
    }

    private async Task<uint> CallAsync(Target t, string member, string signature, Action<MessageWriter> args, bool read = false)
    {
        if (_conn == null)
        {
            var conn = new Connection(Address.Session ?? throw new DBusException("org.freedesktop.DBus.Error.NoServer", "no session bus"));
            await conn.ConnectAsync().ConfigureAwait(false);
            _conn = conn; // kept open for the app's lifetime: the inhibit lives as long as it does
        }
        MessageBuffer msg;
        using (var w = _conn.GetMessageWriter())
        {
            w.WriteMethodCallHeader(destination: t.Service, path: t.Path, @interface: t.Interface, member: member, signature: signature);
            args(w);
            msg = w.CreateMessage();
        }
        if (!read)
        {
            await _conn.CallMethodAsync(msg).ConfigureAwait(false);
            return 0;
        }
        return await _conn.CallMethodAsync(msg, static (Message m, object? _) => m.GetBodyReader().ReadUInt32()).ConfigureAwait(false);
    }
}
