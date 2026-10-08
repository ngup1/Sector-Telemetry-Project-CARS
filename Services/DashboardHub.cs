using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace SectorTelemetry.Services;

/// <summary>Fan-out of snapshots to connected dashboards. A slow client skips frames rather than queueing them.</summary>
public sealed class DashboardHub(ILogger<DashboardHub> logger)
{
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    private sealed class Client(WebSocket socket)
    {
        public readonly WebSocket Socket = socket;
        public readonly SemaphoreSlim Sending = new(1, 1);
    }

    public int ClientCount => _clients.Count;

    public async Task RunAsync(WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        _clients[id] = new Client(socket);
        logger.LogInformation("Dashboard connected ({Count} total)", _clients.Count);
        try
        {
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            logger.LogInformation("Dashboard disconnected ({Count} total)", _clients.Count);
        }
    }

    public void Broadcast(byte[] payload)
    {
        foreach (var (id, client) in _clients)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }
            if (!client.Sending.Wait(0)) continue;
            _ = SendAsync(id, client, payload);
        }
    }

    private async Task SendAsync(Guid id, Client client, byte[] payload)
    {
        try
        {
            await client.Socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            _clients.TryRemove(id, out _);
        }
        finally
        {
            client.Sending.Release();
        }
    }
}
