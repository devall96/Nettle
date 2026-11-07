using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Nettle.Core.Services;

namespace Nettle.Core.Api;

/// <summary>
/// The Jellyfin server's WebSocket (/socket): how the server reaches this app — remote control
/// from other Jellyfin apps ("play this on Nettle"), watched-state changes made elsewhere, and
/// new library items. Only ever connects to the signed-in server; the token travels in a header.
/// Reconnects by itself (with back-off) until <see cref="Stop"/>.
/// </summary>
public sealed class JellyfinSocket : IDisposable
{
    private readonly JellyfinClient _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Raised on a background thread with the message type and its "Data" (may be Undefined).</summary>
    public event Action<string, JsonElement>? Message;
    /// <summary>Raised (background thread) each time a connection is established.</summary>
    public event Action? Connected;

    public bool IsConnected { get; private set; }

    public JellyfinSocket(JellyfinClient client) => _client = client;

    public void Start()
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsConnected = false;
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var uri = _client.SocketUri();
            if (uri == null || _client.TokenHeaderValue == null) return;
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", _client.AuthorizationHeaderValue);
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(15));
                    await ws.ConnectAsync(uri, connect.Token).ConfigureAwait(false);
                }
                failures = 0;
                IsConnected = true;
                Log.Info("server connection (WebSocket) open");
                try { Connected?.Invoke(); } catch (Exception ex) { Log.Warn("socket connected handler: " + ex.Message); }
                await ReceiveAsync(ws, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failures++;
                if (failures <= 3) Log.Info("server connection (WebSocket) dropped: " + ex.Message);
            }
            finally { IsConnected = false; }

            if (ct.IsCancellationRequested) break;
            // 2 s, 5 s, 15 s, then every 30 s while the server is away.
            var wait = failures switch { 0 or 1 => 2, 2 => 5, 3 => 15, _ => 30 };
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
        Log.Info("server connection (WebSocket) closed");
    }

    private async Task ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        Timer? keepAlive = null;
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, r.Count);
                    if (message.Length > 4 * 1024 * 1024) throw new InvalidDataException("message too large");
                } while (!r.EndOfMessage);

                string type;
                JsonElement data;
                try
                {
                    using var doc = JsonDocument.Parse(message.ToArray());
                    type = doc.RootElement.TryGetProperty("MessageType", out var t) ? t.GetString() ?? "" : "";
                    data = doc.RootElement.TryGetProperty("Data", out var d) ? d.Clone() : default;
                }
                catch (JsonException) { continue; }

                if (type == "ForceKeepAlive")
                {
                    // The server drops clients that go quiet: answer at half its timeout.
                    int seconds = data.ValueKind == JsonValueKind.Number ? data.GetInt32() : 60;
                    var period = TimeSpan.FromSeconds(Math.Max(5, seconds / 2.0));
                    keepAlive?.Dispose();
                    keepAlive = new Timer(_ => _ = SendAsync(ws, "{\"MessageType\":\"KeepAlive\"}"), null, TimeSpan.Zero, period);
                    continue;
                }
                if (type == "KeepAlive") continue;
                try { Message?.Invoke(type, data); } catch (Exception ex) { Log.Warn($"socket message {type} failed: {ex.Message}"); }
            }
        }
        finally { keepAlive?.Dispose(); }
    }

    private static readonly SemaphoreSlim SendGate = new(1, 1);

    private static async Task SendAsync(ClientWebSocket ws, string json)
    {
        if (ws.State != WebSocketState.Open) return;
        await SendGate.WaitAsync().ConfigureAwait(false);
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false); }
        catch { /* the receive loop notices a dead connection */ }
        finally { SendGate.Release(); }
    }
}
