using Domain.Enum;
using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace Domain.Services;

using Domain.Parameters;

public class IndicatorBasedEngine(ILogger<IndicatorBasedEngine> logger) : IStrategyEngine
{
    public TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, MarketData marketPhase,
        MarketData marketPattern, MarketData marketEntry, long lastTradeSignalTime, List<PriceZone> zones,
        List<MarketData> history, StrategyParameters? parameters = null, double riskPercent = 1, double maximumRiskAmount = 5)
    {
        parameters ??= new();
        if (marketEntry.OpenTime == lastTradeSignalTime)
        {
            logger.LogWarning(
                "IndicatorEngine: Signal on current candle ({OpenTime}) has already been traded. Skipping.",
                marketEntry.OpenTime);
            return new TradeDecision {Note = "Signal already traded on this candle."};
        }

        var signal = GetTradingSignal(marketPhase, marketPattern, marketEntry, parameters);
        if (signal == ActionKind.Hold)
        {
            return new TradeDecision {Note = "No valid indicator-based signal detected."};
        }

        return PositionSizer.CreateRiskPlan(signal, account.Equity, symbol, marketEntry, history,
            parameters, riskPercent, maximumRiskAmount, "Indicator-based signal confirmed.");
    }

    private ActionKind GetTradingSignal(MarketData phase, MarketData pattern, MarketData entry, StrategyParameters parameters)
    {
        logger.LogInformation("--- Evaluating Indicator-Based Strategy Conditions ---");

        var adxThreshold = parameters.StrongTrendAdx;
        if (!double.IsFinite(pattern.Adx) || pattern.Adx < adxThreshold)
        {
            logger.LogWarning("IndicatorEngine: Trend is too weak (ADX < {Threshold}). No trade allowed.",
                adxThreshold);
            return ActionKind.Hold;
        }

        logger.LogInformation("IndicatorEngine: ADX Check Passed (ADX: {AdxValue} >= {Threshold})", pattern.Adx,
            adxThreshold);

        var isPhaseUpTrend = phase.Close > phase.EmaSlow;
        var isPatternUpTrend = pattern.Close > pattern.EmaSlow;
        var isPhaseDownTrend = phase.Close < phase.EmaSlow;
        var isPatternDownTrend = pattern.Close < pattern.EmaSlow;
        logger.LogInformation("IndicatorEngine: Phase ({Timeframe}m) Trend: IsUpTrend = {IsUpTrend}", parameters.PhaseTimeframeMinutes, isPhaseUpTrend);
        logger.LogInformation("IndicatorEngine: Pattern ({Timeframe}m) Trend: IsUpTrend = {IsUpTrend}", parameters.PatternTimeframeMinutes, isPatternUpTrend);

        var atrMultiplier = parameters.AtrSpikeMultiplier;
        var isVolatileByAtr = entry.Atr > (entry.AtrSma * atrMultiplier);

        var explosiveTrendAdx = parameters.MomentumAdx;
        var isExplosiveByAdx = pattern.Adx > explosiveTrendAdx;

        bool entryBuyTrigger;
        bool entrySellTrigger;

        if (isExplosiveByAdx || isVolatileByAtr)
        {
            logger.LogInformation(
                "IndicatorEngine: Entry Mode: Momentum (Aggressive). Triggered by ADX > {AdxExplosive} or ATR Spike.",
                explosiveTrendAdx);
            entryBuyTrigger = entry.Close > entry.Open;
            entrySellTrigger = entry.Close < entry.Open;
        }
        else
        {
            logger.LogInformation("IndicatorEngine: Entry Mode: Pullback (Conservative)");
            entryBuyTrigger = entry.Low <= entry.EmaFast && entry.Close > entry.Open;
            entrySellTrigger = entry.High >= entry.EmaFast && entry.Close < entry.Open;
        }

        logger.LogInformation(
            "IndicatorEngine: Entry ({Timeframe}m) Trigger Check: BuyTrigger = {BuyTrigger}, SellTrigger = {SellTrigger}",
            parameters.EntryTimeframeMinutes, entryBuyTrigger, entrySellTrigger);

        if (isPhaseUpTrend && isPatternUpTrend && entryBuyTrigger)
        {
            logger.LogInformation("✅ IndicatorEngine: SUCCESS: All conditions for a BUY signal are met.");
            return ActionKind.Buy;
        }

        if (isPhaseDownTrend && isPatternDownTrend && entrySellTrigger)
        {
            logger.LogInformation("✅ IndicatorEngine: SUCCESS: All conditions for a SELL signal are met.");
            return ActionKind.Sell;
        }

        return ActionKind.Hold;
    }
}
