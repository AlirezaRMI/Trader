using System.Net.WebSockets;
using System.Text.Json;
using Trader.Protocol;

namespace Bridge;

public sealed class BackendConnection(
    TerminalServer terminal,
    IConfiguration configuration,
    ILogger<BackendConnection> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var uri = new Uri(configuration["Bridge:BackendUrl"] ?? "ws://127.0.0.1:5080/bridge/ws");
        if (uri.Scheme != "wss" && !(uri.Scheme == "ws" && uri.IsLoopback))
            throw new InvalidOperationException("Remote bridge connections require wss://");
        var token = configuration["Bridge:Token"];
        if (string.IsNullOrWhiteSpace(token))
            token = LocalEnvironment.ReadBridgeKey(configuration["Runtime:DataDirectory"] ?? "data");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Start the local backend first or configure Bridge__Token");

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            using var connectionStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using var sendGate = new SemaphoreSlim(1, 1);
            Task? heartbeat = null;
            try
            {
                await socket.ConnectAsync(uri, stoppingToken);
                logger.LogInformation("Authenticated backend connection established");
                attempt = 0;
                heartbeat = HeartbeatAsync(socket, sendGate, connectionStop.Token);
                while (!connectionStop.IsCancellationRequested)
                {
                    var request = await WebSocketFrames.ReadAsync<RpcRequest>(socket, connectionStop.Token);
                    _ = WireProtocol.EncodeRequest(request);
                    var reply = await terminal.ExecuteAsync(request, connectionStop.Token);
                    await SendAsync(socket, sendGate, reply, connectionStop.Token);
                }
            }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException
                                          or JsonException or ArgumentException)
            {
                if (!stoppingToken.IsCancellationRequested)
                    logger.LogWarning("Backend connection lost; retrying (no order replay)");
            }
            finally
            {
                connectionStop.Cancel();
                socket.Abort();
                if (heartbeat is not null)
                    try
                    {
                        await heartbeat;
                    }
                    catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
                    {
                    }
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * Math.Pow(2, Math.Min(attempt++, 4)))),
                    stoppingToken);
        }
    }

    private async Task HeartbeatAsync(WebSocket socket, SemaphoreSlim sendGate, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            await SendAsync(socket, sendGate,
                new RpcReply(Guid.Empty, true, JsonSerializer.SerializeToElement(new { }))
                    {Kind = "status", TerminalConnected = terminal.IsConnected},
                cancellationToken);
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private static async Task SendAsync(WebSocket socket, SemaphoreSlim gate, RpcReply reply, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await WebSocketFrames.WriteAsync(socket, reply, token);
        }
        finally
        {
            gate.Release();
        }
    }
}