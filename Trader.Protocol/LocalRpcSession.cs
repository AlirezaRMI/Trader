using System.Diagnostics;
using System.Text;

namespace Trader.Protocol;

/// <summary>Single-flight local RPC; a timed-out stream is closed, never reused with a late reply.</summary>
public sealed class LocalRpcSession : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly LineFrameReader _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _readerTask;
    private readonly object _sync = new();
    private (Guid Id, TaskCompletionSource<RpcReply> Completion)? _pending;
    public bool IsConnected => !_stop.IsCancellationRequested;

    public LocalRpcSession(Stream stream)
    {
        _stream = stream;
        _reader = new(stream);
        _readerTask = ReceiveAsync();
    }

    public async Task<RpcReply> ExchangeAsync(RpcRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var frame = Encoding.UTF8.GetBytes(WireProtocol.EncodeRequest(request) + "\n");
        await _gate.WaitAsync(cancellationToken);
        var written = false;
        try
        {
            if (!IsConnected) throw new EndOfStreamException("Terminal disconnected");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            deadline.CancelAfter(timeout);
            var completion = new TaskCompletionSource<RpcReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync) _pending = (request.Id, completion);
            written = true; // A partial write is also uncertain for a mutation.
            await _stream.WriteAsync(frame, deadline.Token);
            return await completion.Task.WaitAsync(deadline.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _stop.Cancel();
            _stream.Dispose();
            return new(request.Id, false, System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                "Terminal connection lost or response timed out")
            { Indeterminate = written, TerminalConnected = false };
        }
        finally
        {
            lock (_sync) _pending = null;
            _gate.Release();
        }
    }

    private async Task ReceiveAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var reply = WireProtocol.ParseReply(await _reader.ReadAsync(_stop.Token));
                lock (_sync)
                    if (_pending is { } pending && pending.Id == reply.Id)
                        pending.Completion.TrySetResult(reply);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException or DecoderFallbackException)
        {
            lock (_sync) _pending?.Completion.TrySetException(new IOException("Terminal disconnected", e));
        }
        finally
        {
            _stop.Cancel();
            _stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _stream.Dispose();
        await _readerTask;
        // The exchange owner may still be unwinding; do not dispose its semaphore underneath it.
    }
}
