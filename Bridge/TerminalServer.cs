using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Trader.Protocol;

namespace Bridge;

public sealed class TerminalServer(int port, ILogger<TerminalServer> logger) : BackgroundService
{
    private readonly object _sync = new();
    private LocalRpcSession? _session;
    private int _boundPort;
    private readonly TcpListener _listener = new(IPAddress.Loopback, port);
    public int BoundPort => Volatile.Read(ref _boundPort);

    public bool IsConnected
    {
        get
        {
            lock (_sync) return _session?.IsConnected == true;
        }
    }

    public async Task<RpcReply> ExecuteAsync(RpcRequest request, CancellationToken cancellationToken)
    {
        LocalRpcSession? session;
        lock (_sync) session = _session;
        if (session?.IsConnected != true)
            return new(request.Id, false, JsonSerializer.SerializeToElement(new { }), "MT4 terminal is not connected");
        return await session.ExchangeAsync(request, TimeSpan.FromSeconds(8), cancellationToken);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _listener.Start(1);
        Volatile.Write(ref _boundPort, ((IPEndPoint)_listener.LocalEndpoint).Port);
        logger.LogInformation("MT4 listener bound to 127.0.0.1:{Port}", BoundPort);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                client.NoDelay = true;
                var session = new LocalRpcSession(client.GetStream());
                LocalRpcSession? old;
                lock (_sync)
                {
                    old = _session;
                    _session = session;
                }

                if (old is not null) await old.DisposeAsync();
                logger.LogInformation("MT4 terminal connected");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _listener.Stop();
            LocalRpcSession? session;
            lock (_sync)
            {
                session = _session;
                _session = null;
            }

            if (session is not null) await session.DisposeAsync();
        }
    }
}
