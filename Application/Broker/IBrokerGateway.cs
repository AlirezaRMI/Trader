using System.Text.Json;

namespace Application.Broker;

public sealed record BrokerConnection(bool AgentConnected, bool TerminalConnected, DateTimeOffset? LastSeen, double? LatencyMs, string? Error);

public interface IBrokerGateway
{
    BrokerConnection Connection { get; }
    Task<JsonElement> InvokeAsync(string method, string arguments, CancellationToken cancellationToken = default);
    // A durable execution intent supplies its identity; read-only requests generate a new one.
    Task<JsonElement> ExecuteAsync(Guid requestId, string method, string arguments, CancellationToken cancellationToken = default);
}

public sealed class BrokerException(string message, bool mayHaveExecuted = false) : Exception(message)
{
    public bool MayHaveExecuted { get; } = mayHaveExecuted;
}
