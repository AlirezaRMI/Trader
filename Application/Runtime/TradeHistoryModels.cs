using Domain.Enum;

namespace Application.Runtime;

public sealed record TradeContext(long AccountId, string BrokerServer, long Ticket, string Currency, bool IsDemo)
{
    public Guid? IntentId { get; init; }
    public long? SignalCandleTime { get; init; }
    public DateTimeOffset? RequestedAt { get; init; }
    public TradeDecision? Decision { get; init; }
    public RuntimeSettings? Settings { get; init; }
    public string Source { get; init; } = "BrokerSnapshot";
}

public sealed record ClosedTrade(long AccountId, string BrokerServer, BrokerPosition Position, TradeContext? Context);
public sealed record TradeAccountPerformance(long AccountId, string BrokerServer, string? Currency, bool? IsDemo,
    int Trades, int Wins, int Losses, double NetProfit);
public sealed record TradeHistoryPage(IReadOnlyList<ClosedTrade> Items, string? NextCursor,
    IReadOnlyList<TradeAccountPerformance> Accounts);
public sealed record JournalPage(IReadOnlyList<JournalEntry> Items, long? NextBefore);
public sealed record TradeHistorySync(bool Supported, bool InProgress, int LoadedBrokerRows, int RemainingBrokerRows,
    DateTimeOffset? LastSyncedAt, string? Error);

public static class BrokerPositionValidation
{
    public static bool IsValid(BrokerPosition position, bool closed) => position.Ticket > 0 &&
        !string.IsNullOrWhiteSpace(position.Symbol) && position.Side is "Buy" or "Sell" &&
        double.IsFinite(position.Lots) && position.Lots > 0 && double.IsFinite(position.EntryPrice) && position.EntryPrice > 0 &&
        double.IsFinite(position.StopLoss) && position.StopLoss >= 0 && double.IsFinite(position.TakeProfit) && position.TakeProfit >= 0 &&
        double.IsFinite(position.Profit) && position.OpenTime > 0 && (closed ? position.CloseTime > 0 : position.CloseTime == 0) &&
        (position.ClosePrice is null || double.IsFinite(position.ClosePrice.Value) && position.ClosePrice > 0) &&
        (position.GrossProfit is null || double.IsFinite(position.GrossProfit.Value)) &&
        (position.Commission is null || double.IsFinite(position.Commission.Value)) &&
        (position.Swap is null || double.IsFinite(position.Swap.Value)) &&
        (position.PriceDigits is null or >= 0 and <= 12);
}
