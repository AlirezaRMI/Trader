using Domain.Enum;
using Domain.Trading;

namespace Domain;

public sealed record OrderId(long Value);

public record TradeRequest(
    Symbol Symbol,
    ActionKind Action,
    double PositionSizeLots,
    double StopLossPrice,
    double TakeProfitPrice,
    string Comment
);

public record TradeExecutionResult
{
    public bool IsSuccess { get; }
    public OrderId? OrderId { get; }
    public string? Note { get; }
    public string? Error { get; }

    private TradeExecutionResult(bool isSuccess, OrderId? orderId, string? note, string? error)
    {
        IsSuccess = isSuccess;
        OrderId = orderId;
        Note = note;
        Error = error;
    }

    public static TradeExecutionResult Success(OrderId orderId, string? note = "Executed successfully.")
    {
        return new TradeExecutionResult(true, orderId, note, null);
    }

    public static TradeExecutionResult Failure(string error)
    {
        return new TradeExecutionResult(false, null, null, error);
    }
}