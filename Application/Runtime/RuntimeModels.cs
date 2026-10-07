using Application.Broker;
using Domain.Enum;

namespace Application.Runtime;

using Domain.Parameters;

public sealed record RuntimeSettings
{
    public string Symbol { get; init; } = "";
    public int AnalysisIntervalSeconds { get; init; } = 30;
    public int PositionIntervalSeconds { get; init; } = 5;
    public int DailyTradeLimit { get; init; } = 10;
    public string RiskProfileId { get; init; } = "Custom";
    public bool ObservationOnly { get; init; }
    public double RiskPercent { get; init; } = 1;
    public double MaximumRiskAmount { get; init; } = 5;
    public double MaximumSpreadPoints { get; init; } = 30;
    public double DailyDrawdownLimitPercent { get; init; } = 3;
    public double TotalDrawdownLimitPercent { get; init; } = 10;
    public int DrawdownCooldownHours { get; init; } = 24;
    public bool AllowLiveAccount { get; init; }
    public bool TelegramEnabled { get; init; } = true;
    public StrategyParameters Strategy { get; init; } = new();
}

public sealed record BrokerPosition(long Ticket, string Symbol, string Side, double Lots, double EntryPrice,
    double StopLoss, double TakeProfit, double Profit, long OpenTime, long CloseTime, string Comment)
{
    public double? ClosePrice { get; init; }
    public double? GrossProfit { get; init; }
    public double? Commission { get; init; }
    public double? Swap { get; init; }
    public int? PriceDigits { get; init; }
}
public sealed record JournalEntry(long Id, DateTimeOffset Timestamp, string Level, string EventType, string Message, string? Symbol, string? Detail);
public sealed record RiskCheckpoint(long AccountId, string BrokerServer, DateTimeOffset Day,
    double DailyPeak, double TotalPeak, DateTimeOffset? BlockedUntil, bool Halted)
{
    public bool BlocksEntries(DateTimeOffset now) => Halted || BlockedUntil > now;
    public RiskCheckpoint Observe(double equity, DateTimeOffset now, double dailyLimit, double totalLimit, int cooldownHours)
    {
        if (!double.IsFinite(equity) || !double.IsFinite(DailyPeak) || DailyPeak <= 0 ||
            !double.IsFinite(TotalPeak) || TotalPeak <= 0) throw new InvalidOperationException("Invalid equity reference");
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var dailyPeak = Day == day ? Math.Max(DailyPeak, equity) : Math.Max(equity, .00000001);
        var totalPeak = Math.Max(TotalPeak, equity);
        var until = 100 * (dailyPeak - equity) / dailyPeak >= dailyLimit && !(BlockedUntil > now) ? now.AddHours(cooldownHours) : BlockedUntil;
        return this with {Day = day, DailyPeak = dailyPeak, TotalPeak = totalPeak, BlockedUntil = until,
            Halted = Halted || 100 * (totalPeak - equity) / totalPeak >= totalLimit};
    }
}
public sealed record TradeIntent(Guid Id, long AccountId, string Symbol, long CandleTime, string Side,
    DateTimeOffset CreatedAt, string Status, long? Ticket, string? Detail)
{
    public string BrokerServer { get; init; } = "";
}
public sealed record AnalysisSnapshot(string Symbol, string Engine, string Action, string Reason,
    DateTimeOffset Timestamp, MarketData Phase, MarketData Pattern, MarketData Entry,
    SymbolDetails Specification, IReadOnlyList<PriceZone> Zones, IReadOnlyList<MarketData> History,
    TradeDecision Decision, double EstimatedRisk, double SpreadPoints);
public sealed record RuntimeSnapshot
{
    public long Revision { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? LastCycleAt { get; init; }
    public DateTimeOffset? LastAnalysisAt { get; init; }
    public bool EntriesEnabled { get; init; }
    public string State { get; init; } = "Paused";
    public string? LastError { get; init; }
    public string? ExecutionPolicyError { get; init; }
    public BrokerConnection Broker { get; init; } = new(false, false, null, null, null);
    public RuntimeSettings Settings { get; init; } = new();
    public AccountInfo? Account { get; init; }
    public AnalysisSnapshot? Analysis { get; init; }
    public IReadOnlyList<BrokerPosition> Positions { get; init; } = [];
    public IReadOnlyList<TradeIntent> UnresolvedIntents { get; init; } = [];
    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];
    public int ConfirmedTradesToday { get; init; }
    public bool OrdersEnabledAtTerminal { get; init; }
    public bool TrailingEnabledAtTerminal { get; init; }
    public bool TelegramConfigured { get; init; }
    public bool TelegramDeliveryEnabled { get; init; }
    public string? TelegramError { get; init; }
    public int PendingNotifications { get; init; }
    public double? LastCycleDurationMs { get; init; }
    public TradeHistorySync HistorySync { get; init; } = new(false, false, 0, 0, null, null);
    public RiskCheckpoint? Risk { get; init; }
}

public static class SettingsValidator
{
    public static string? Validate(RuntimeSettings settings)
    {
        if (settings.Symbol is null || settings.Symbol.Length > 64 || settings.Symbol.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '#')))
            return "Invalid symbol";
        if (settings.AnalysisIntervalSeconds is < 10 or > 3600 || settings.PositionIntervalSeconds is < 2 or > 60)
            return "Invalid polling intervals";
        if (settings.RiskProfileId is null || settings.RiskProfileId.Length is < 1 or > 32 ||
            settings.RiskProfileId.Any(c => !char.IsAsciiLetterOrDigit(c))) return "Invalid risk profile identity";
        if ((settings.ObservationOnly ? settings.DailyTradeLimit != 0 || settings.RiskPercent != 0 || settings.MaximumRiskAmount != 0 :
            settings.DailyTradeLimit is < 1 or > 100 || settings.RiskPercent <= 0 || settings.MaximumRiskAmount <= 0) ||
            !double.IsFinite(settings.RiskPercent) || settings.RiskPercent > 5 ||
            !double.IsFinite(settings.MaximumRiskAmount) || settings.MaximumRiskAmount > 10000 ||
            !double.IsFinite(settings.MaximumSpreadPoints) || settings.MaximumSpreadPoints is <= 0 or > 10000)
            return "Invalid risk or execution limits";
        if (settings.Strategy is null) return "Strategy parameters are required";
        if (!double.IsFinite(settings.DailyDrawdownLimitPercent) || settings.DailyDrawdownLimitPercent <= 0 || settings.DailyDrawdownLimitPercent >= 100 ||
            !double.IsFinite(settings.TotalDrawdownLimitPercent) || settings.TotalDrawdownLimitPercent <= 0 || settings.TotalDrawdownLimitPercent >= 100 ||
            settings.DrawdownCooldownHours is < 1 or > 720) return "Invalid equity drawdown limits";
        if (settings.Strategy.Validate() is { } strategyError) return strategyError;
        return null;
    }
}
