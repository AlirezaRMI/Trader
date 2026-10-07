using System.Text.Json;

namespace Trader.Protocol;

public sealed record RpcRequest(Guid Id, string Method, string Arguments)
{
    public int Version { get; init; } = 1;
}

public sealed record RpcReply(Guid Id, bool Success, JsonElement Data, string? Error = null)
{
    public int Version { get; init; } = 1;
    public string Kind { get; init; } = "response";
    public bool TerminalConnected { get; init; }
    public bool Indeterminate { get; init; }
}

public static class WireProtocol
{
    public const int MaximumFrameBytes = 1024 * 1024;
    public static string EncodeRequest(RpcRequest request)
    {
        if (request.Version != 1 || request.Id == Guid.Empty || string.IsNullOrWhiteSpace(request.Method) ||
            request.Method.Any(c => !(c is >= 'A' and <= 'Z' or '_')) ||
            request.Arguments.IndexOfAny(['\r', '\n', '|', '\0']) >= 0)
            throw new ArgumentException("Invalid broker command");
        var frame = $"1|{request.Id:N}|{request.Method}|{request.Arguments}";
        if (System.Text.Encoding.UTF8.GetByteCount(frame) > MaximumFrameBytes)
            throw new ArgumentException("Broker command is too large");
        return frame;
    }

    public static RpcReply ParseReply(string frame)
    {
        var parts = frame.Split('|', 4);
        if (parts.Length != 4 || parts[0] != "1" || !Guid.TryParseExact(parts[1], "N", out var id) ||
            id == Guid.Empty || parts[2] is not ("OK" or "ERROR"))
            throw new InvalidDataException("Invalid broker response envelope");
        using var document = JsonDocument.Parse(parts[3]);
        var data = document.RootElement.Clone();
        var error = parts[2] == "ERROR"
            ? (data.TryGetProperty("message", out var message) ? message.GetString() : "Broker rejected command")
            : null;
        return new RpcReply(id, parts[2] == "OK", data, error)
        {
            TerminalConnected = true,
            Indeterminate = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("indeterminate", out var uncertain) && uncertain.ValueKind == JsonValueKind.True
        };
    }
}
