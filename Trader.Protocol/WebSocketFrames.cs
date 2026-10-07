using System.Net.WebSockets;
using System.Text.Json;

namespace Trader.Protocol;

public static class WebSocketFrames
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadAsync<T>(WebSocket socket, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new EndOfStreamException("Agent disconnected");
            if (result.MessageType != WebSocketMessageType.Text || payload.Length + result.Count > WireProtocol.MaximumFrameBytes)
                throw new InvalidDataException("Invalid or oversized agent message");
            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return JsonSerializer.Deserialize<T>(payload.GetBuffer().AsSpan(0, (int)payload.Length), JsonOptions)
                       ?? throw new InvalidDataException("Empty agent message");
        }
    }

    public static async Task WriteAsync<T>(WebSocket socket, T message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > WireProtocol.MaximumFrameBytes)
            throw new InvalidDataException("Oversized agent message");
        await socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
    }
}
