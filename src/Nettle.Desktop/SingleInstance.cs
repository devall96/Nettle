using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Nettle.Desktop;

/// <summary>
/// Only one Nettle runs per user: two copies would fight over the same settings, image cache
/// and saved sign-ins. The first copy holds a lock file in its data folder; a second copy finds
/// it locked, pokes the first one over a local pipe (it comes to the front) and exits.
/// Nothing leaves the machine: the pipe is local to this user's session.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(FileStream lockFile, string pipeName)
    {
        _lock = lockFile;
        _pipeName = pipeName;
    }

    private static string PipeName(string dataDir)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir + "|" + Environment.UserName)))[..16];
        return "nettle-" + hash.ToLowerInvariant();
    }

    /// <summary>Returns the lock if this is the first copy, or null if Nettle is already running.</summary>
    public static SingleInstance? TryAcquire(string dataDir)
    {
        try
        {
            var fs = new FileStream(Path.Combine(dataDir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new SingleInstance(fs, PipeName(dataDir));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Asks the running copy to show itself. Best effort.</summary>
    public static void SignalExisting(string dataDir)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(dataDir), PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(1);
        }
        catch { /* the other copy may be starting or closing */ }
    }

    /// <summary>Calls <paramref name="onActivate"/> whenever another copy is started.</summary>
    public void Listen(Action onActivate)
    {
        var t = new Thread(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    server.ReadByte();
                    onActivate();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Nettle.Core.Services.Log.Warn("single-instance pipe: " + ex.Message);
                    try { await Task.Delay(2000, _stop.Token); } catch { return; }
                }
            }
        }) { IsBackground = true, Name = "single instance" };
        t.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _lock.Dispose();
    }
}
