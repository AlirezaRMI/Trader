using Domain.Enum;
using Domain.Parameters;

namespace Application.Research;

public sealed record BacktestRequest
{
    public List<MarketData> Candles { get; init; } = [];
    public List<MarketData>? PhaseCandles { get; init; }
    public List<MarketData>? PatternCandles { get; init; }
    public List<double>? SpreadPointsSeries { get; init; }
    public SymbolDetails Symbol { get; init; } = new();
    public StrategyParameters Parameters { get; init; } = new();
    public double InitialEquity { get; init; } = 1000;
    public double RiskPercent { get; init; } = 1;
    public double MaximumRiskAmount { get; init; } = 5;
    public double SpreadPoints { get; init; } = 10;
    public double SlippagePoints { get; init; } = 1;
    public double CommissionPerLot { get; init; } = 7;
    public int DailyTradeLimit { get; init; } = 10;
    public double MaximumSpreadPoints { get; init; } = 30;
    public double DailyDrawdownLimitPercent { get; init; } = 3;
    public double TotalDrawdownLimitPercent { get; init; } = 10;
    public int DrawdownCooldownHours { get; init; } = 24;
}
public sealed record BacktestTrade(long EntryTime, long ExitTime, string Side, double Lots, double EntryPrice,
    double ExitPrice, double Profit, double Costs, string ExitReason);
public sealed record EquityPoint(long Time, double Equity);
public sealed record BacktestResult(double InitialEquity, double FinalEquity, double NetProfit, double ReturnPercent,
    double MaximumDrawdownPercent, double WinRatePercent, double? ProfitFactor, double Expectancy,
    IReadOnlyList<BacktestTrade> Trades, IReadOnlyList<EquityPoint> EquityCurve, string Assumptions);
public sealed record OptimizationRequest
{
    public BacktestRequest Backtest { get; init; } = new();
    public double TrainingFraction { get; init; } = .7;
    public double MaximumDrawdownPercent { get; init; } = 15;
    public int MinimumTrainingTrades { get; init; } = 5;
    public List<double> StopAtrMultipliers { get; init; } = [1, 1.5, 2];
    public List<double> RewardRiskRatios { get; init; } = [1.5, 2, 3];
}
public sealed record OptimizationCandidate(StrategyParameters Parameters, BacktestResult Training, double Score);
public sealed record OptimizationResult(long ValidationStartTime, int CandidatesEvaluated,
    OptimizationCandidate? Selected, BacktestResult? Validation, IReadOnlyList<OptimizationCandidate> Candidates);
