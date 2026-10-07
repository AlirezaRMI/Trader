using System.Net.WebSockets;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using Application.Broker;
using Trader.Protocol;

namespace Infrastructure.Bridge;

public sealed class AgentGateway(TimeSpan? requestTimeout = null) : IBrokerGateway
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private Session? _session;
    private string? _lastError;
    private sealed class Session(WebSocket socket, CancellationToken token)
    {
        public WebSocket Socket { get; } = socket;
        public CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        public ConcurrentDictionary<Guid, TaskCompletionSource<RpcReply>> Pending { get; } = new();
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public bool TerminalConnected { get; set; }
        public double? LatencyMs { get; set; }
    }

    public BrokerConnection Connection
    {
        get
        {
            lock (_sync)
            {
                var session = _session;
                var connected = session is not null && session.Socket.State == WebSocketState.Open &&
                                DateTimeOffset.UtcNow - session.LastSeen < TimeSpan.FromSeconds(15);
                return new(connected, connected && session!.TerminalConnected, session?.LastSeen, session?.LatencyMs, _lastError);
            }
        }
    }
    public Task<JsonElement> InvokeAsync(string method, string arguments, CancellationToken cancellationToken = default) =>
        ExecuteAsync(Guid.NewGuid(), method, arguments, cancellationToken);
    public async Task<JsonElement> ExecuteAsync(Guid requestId, string method, string arguments, CancellationToken cancellationToken = default)
    {
        var request = new RpcRequest(requestId, method, arguments);
        _ = WireProtocol.EncodeRequest(request);
        var mutation = method is "OPEN_ORDER" or "CLOSE_ORDER" or "MODIFY_ORDER";
        await _requestGate.WaitAsync(cancellationToken);
        Session? session = null;
        var written = false;
        try
        {
            lock (_sync) session = _session;
            if (session is null || session.Socket.State != WebSocketState.Open)
                throw new BrokerException("MT4 agent is not connected");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Stop.Token);
            deadline.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(12));
            var completion = new TaskCompletionSource<RpcReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!session.Pending.TryAdd(requestId, completion))
                throw new BrokerException("Duplicate request identity");
            var start = Stopwatch.GetTimestamp();
            written = true;
            await WebSocketFrames.WriteAsync(session.Socket, request, deadline.Token);
            var reply = await completion.Task.WaitAsync(deadline.Token);
            lock (_sync) session.LatencyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (!reply.Success)
                throw new BrokerException(reply.Error ?? "Broker rejected the command", mutation && reply.Indeterminate);
            return reply.Data;
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
            session?.Stop.Cancel();
            session?.Socket.Abort();
            throw new BrokerException("Agent disconnected or response timed out", mutation && written);
        }
        finally
        {
            session?.Pending.TryRemove(requestId, out _);
            _requestGate.Release();
        }
    }

    public async Task AttachAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var session = new Session(socket, cancellationToken);
        Session? previous;
        lock (_sync) { previous = _session; _session = session; _lastError = null; }
        previous?.Stop.Cancel();
        previous?.Socket.Abort();
        try
        {
            while (!session.Stop.IsCancellationRequested)
            {
                var reply = await WebSocketFrames.ReadAsync<RpcReply>(socket, session.Stop.Token);
                if (reply.Version != 1 || reply.Kind is not ("response" or "status"))
                    throw new InvalidDataException("Unsupported agent protocol");
                lock (_sync)
                {
                    session.LastSeen = DateTimeOffset.UtcNow;
                    session.TerminalConnected = reply.TerminalConnected;
                }
                if (reply.Kind == "response" && session.Pending.TryGetValue(reply.Id, out var completion))
                    completion.TrySetResult(reply);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or JsonException or ObjectDisposedException)
        {
            lock (_sync) if (ReferenceEquals(_session, session)) _lastError = "Agent connection closed";
        }
        finally
        {
            session.Stop.Cancel();
            socket.Abort();
            foreach (var pending in session.Pending.Values)
                pending.TrySetException(new IOException("Agent connection closed"));
            lock (_sync) if (ReferenceEquals(_session, session)) _session = null;
        }
    }
}
