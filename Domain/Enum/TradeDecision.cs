
namespace Domain.Enum;

public record TradeDecision
{
    public ActionKind Action { get; init; } = ActionKind.Hold;
    public double EntryPrice { get; init; }
    public double StopLossPrice { get; init; }
    public double TakeProfitPrice { get; init; }
    public double PositionSizeLots { get; init; }
    public string Note { get; init; } = "No signal.";
}
