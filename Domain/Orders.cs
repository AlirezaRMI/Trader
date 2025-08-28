using Domain.Enum;
using Domain.Trading;

namespace Domain;

public sealed record OrderId(long Value);

public sealed record TradeRequest(
    Symbol Symbol,
    ActionKind Side,
    double Lots,
    double Sl,
    double Tp,
    string Comment
);

public sealed record TradeExecutionResult(
    bool IsSuccess,
    OrderId? OrderId,
    string? Error
);

public interface IOrderExecutionPort
{
    Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct);
    Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct);
}