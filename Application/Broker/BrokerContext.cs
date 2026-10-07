using System.Globalization;
using System.Text;
using System.Text.Json;
using Domain.Enum;

namespace Application.Broker;

/// <summary>Binds each operation to one broker account and one EA instance.</summary>
public static class BrokerContext
{
    public static string Bind(AccountInfo account, string arguments)
    {
        if (account.AccountId <= 0 || string.IsNullOrWhiteSpace(account.Server) ||
            string.IsNullOrWhiteSpace(account.TerminalSession) || account.TerminalSession.Length > 128 ||
            account.TerminalSession.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new BrokerException("Broker account/session identity is missing");
        return account.AccountId.ToString(CultureInfo.InvariantCulture) + ";" +
            Convert.ToHexString(Encoding.UTF8.GetBytes(account.Server)).ToLowerInvariant() + ";" +
            account.TerminalSession + ";" + arguments;
    }

    public static JsonElement Unwrap(AccountInfo account, JsonElement reply, bool mutation = false)
    {
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("context", out var context) ||
            !context.TryGetProperty("accountId", out var id) || !id.TryGetInt64(out var accountId) || accountId != account.AccountId ||
            !context.TryGetProperty("server", out var server) || server.GetString() != account.Server ||
            !context.TryGetProperty("terminalSession", out var session) || session.GetString() != account.TerminalSession ||
            !reply.TryGetProperty("value", out var value))
            throw new BrokerException("Broker account/server/session changed", mutation);
        return value;
    }

    public static async Task<JsonElement> InvokeAsync(IBrokerGateway broker, AccountInfo account,
        string method, string arguments, CancellationToken cancellationToken) =>
        Unwrap(account, await broker.InvokeAsync(method, Bind(account, arguments), cancellationToken));
}
