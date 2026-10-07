using Domain.Enum;
using Domain.Functions;
using Domain.Parameters;
using Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Strategy;

public sealed record StrategyInput(AccountInfo Account, SymbolDetails Symbol, IReadOnlyList<MarketData> Phase,
    IReadOnlyList<MarketData> Pattern, IReadOnlyList<MarketData> Entry, double Bid, double Ask, int QuoteAgeSeconds,
    long LastConfirmedSignal, StrategyParameters Parameters, double RiskPercent, double MaximumRiskAmount)
{
    public double MaximumSpreadPoints { get; init; } = 30;
}
public sealed record StrategyAnalysis(string Engine, TradeDecision Decision, MarketData Phase, MarketData Pattern,
    MarketData Entry, IReadOnlyList<PriceZone> Zones);

/// <summary>One causal policy path shared by research and deployment. Own one instance per independent run.</summary>
public sealed class StrategyPipeline
{
    private readonly IndicatorBasedEngine _indicator = new(NullLogger<IndicatorBasedEngine>.Instance);
    private readonly PriceActionEngine _priceAction = new(NullLogger<PriceActionEngine>.Instance);
    private readonly PriceActionAnalyzer _zones = new(NullLogger<PriceActionAnalyzer>.Instance);
    private readonly Queue<double> _spreads = new();
    private (long Account, string Server, string Session, string Symbol)? _spreadScope;

    public StrategyAnalysis Evaluate(StrategyInput input)
    {
        var p = input.Parameters;
        if (p.Validate() is { } parameterError) throw new ArgumentException(parameterError);
        var scope = (input.Account.AccountId, input.Account.Server, input.Account.TerminalSession, input.Symbol.SymbolName);
        if (_spreadScope != scope) { _spreads.Clear(); _spreadScope = scope; }
        ValidateTimeline(input.Entry, p.EntryTimeframeMinutes);
        var cutoff = checked(input.Entry[^1].OpenTime + p.EntryTimeframeMinutes * 60L);
        ValidateHigherTimeline(input.Phase, p.PhaseTimeframeMinutes, input.Entry, p.EntryTimeframeMinutes, cutoff);
        ValidateHigherTimeline(input.Pattern, p.PatternTimeframeMinutes, input.Entry, p.EntryTimeframeMinutes, cutoff);
        var phaseParameters = p.RequireMacroTrend ? p with {FastEmaPeriod = p.MacroFastEmaPeriod,
            SlowEmaPeriod = p.MacroSlowEmaPeriod, HistoryBars = p.PhaseHistoryBars} : p;
        var phase = IndicatorCalculator.Calculate(input.Phase, phaseParameters, input.Bid, input.Ask, input.QuoteAgeSeconds);
        var pattern = IndicatorCalculator.Calculate(input.Pattern, p, input.Bid, input.Ask, input.QuoteAgeSeconds);
        var entry = IndicatorCalculator.Calculate(input.Entry, p, input.Bid, input.Ask, input.QuoteAgeSeconds);
        var spread = (input.Ask - input.Bid) / input.Symbol.Point;
        var spreadLimit = _spreads.Count >= p.SpreadLookbackSamples ?
            Math.Min(input.MaximumSpreadPoints, _spreads.Average() * p.SpreadSpikeMultiplier) : input.MaximumSpreadPoints;
        var history = input.Entry.TakeLast(p.HistoryBars).Reverse().ToList();
        var zones = _zones.DetectZones(history, Math.Max(entry.Atr * p.ZoneMergeAtr, input.Symbol.Point * 5), p.MinimumZoneStrength);
        var supervisor = new MarketSupervisor(_indicator, _priceAction, NullLogger<MarketSupervisor>.Instance);
        var engine = supervisor.SelectStrategy(pattern, zones, p);
        var decision = engine.Evaluate(input.Account, input.Symbol, phase, pattern, entry,
            input.LastConfirmedSignal, zones, history, p, input.RiskPercent, input.MaximumRiskAmount);
        // Observe retest state even while paused or an indicator gate rejects the current bar.
        var confirmation = p.RequireBreakoutRetest && engine != _priceAction ?
            _priceAction.Evaluate(input.Account, input.Symbol, phase, pattern, entry,
                input.LastConfirmedSignal, zones, history, p, input.RiskPercent, input.MaximumRiskAmount) : decision;
        string? gate = input.QuoteAgeSeconds > p.MaximumQuoteAgeSeconds ? "Market quote is stale" :
            !double.IsFinite(spread) || !double.IsFinite(input.MaximumSpreadPoints) || input.MaximumSpreadPoints <= 0 || spread > spreadLimit ?
                "Spread exceeds the absolute or observed relative limit" :
            pattern.Adx < p.MinimumEntryAdx ? "ADX is below the entry trend threshold" :
            Math.Abs(entry.Close - entry.Open) / entry.Close < p.MinimumBodyRatio ? "Closed candle body is below the configured relative threshold" : null;
        if (decision.Action != ActionKind.Hold && gate is null)
        {
            var macroAligned = decision.Action == ActionKind.Buy ? phase.Close > phase.EmaSlow && phase.EmaFast > phase.EmaSlow :
                phase.Close < phase.EmaSlow && phase.EmaFast < phase.EmaSlow;
            if (p.RequireMacroTrend && !macroAligned) gate = "Macro EMA regime does not confirm this direction";
            else if (p.RequireBreakoutRetest && confirmation.Action != decision.Action)
                gate = "Waiting for a later closed-bar breakout retest in the signal direction";
        }
        if (gate is not null) decision = new() {Note = gate};
        if (double.IsFinite(spread) && spread >= 0 && input.QuoteAgeSeconds <= p.MaximumQuoteAgeSeconds)
        {
            _spreads.Enqueue(spread);
            while (_spreads.Count > p.SpreadLookbackSamples) _spreads.Dequeue();
        }
        return new(engine == _indicator ? "Indicator" : "PriceAction", decision, phase, pattern, entry, zones);
    }

    private static void ValidateTimeline(IReadOnlyList<MarketData> candles, int minutes)
    {
        IndicatorCalculator.ValidateCandles(candles);
        if (candles.Any(c => c.OpenTime % (minutes * 60L) != 0))
            throw new ArgumentException("History timestamps do not align to their configured timeframe");
    }

    private static void ValidateHigherTimeline(IReadOnlyList<MarketData> higher, int minutes,
        IReadOnlyList<MarketData> entry, int entryMinutes, long cutoff)
    {
        ValidateTimeline(higher, minutes);
        var latest = higher[^1].OpenTime;
        if (latest > cutoff - minutes * 60L)
            throw new ArgumentException("Higher-timeframe history is not closed as of the entry candle");
        // Missing weekends/sessions are allowed. Reject staleness only when entry observations
        // prove a later complete higher-timeframe bar exists, not by fabricating bars in a gap.
        var observed = CandleAggregator.Aggregate(entry, entryMinutes, minutes);
        if (observed.Count > 0 && observed[^1].OpenTime > latest)
            throw new ArgumentException("Higher-timeframe history is stale relative to entry observations");
    }
}
