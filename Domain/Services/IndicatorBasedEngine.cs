using Domain.Enum;
using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace Domain.Services;

public class IndicatorBasedEngine(ILogger<IndicatorBasedEngine> logger) : IStrategyEngine
{
    private const double RiskPerTradeUSD = 5.0;
    private const double RiskPercentage = 0.01;

    public TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, MarketData marketPhase,
        MarketData marketPattern, MarketData marketEntry, long lastTradeSignalTime, List<PriceZone> zones,
        List<MarketData> history)
    {
        if (marketEntry.OpenTime == lastTradeSignalTime)
        {
            logger.LogWarning(
                "IndicatorEngine: Signal on current candle ({OpenTime}) has already been traded. Skipping.",
                marketEntry.OpenTime);
            return new TradeDecision {Note = "Signal already traded on this candle."};
        }

        var signal = GetTradingSignal(marketPhase, marketPattern, marketEntry);
        if (signal == ActionKind.Hold)
        {
            return new TradeDecision {Note = "No valid indicator-based signal detected."};
        }

        double stopLossAtrMultiplier = 2.0;
        double stopLossDistance = marketEntry.Atr * stopLossAtrMultiplier;

        double stopLossPrice;
        if (signal == ActionKind.Buy)
        {
            stopLossPrice = marketEntry.Low - stopLossDistance;
        }
        else
        {
            stopLossPrice = marketEntry.High + stopLossDistance;
        }
        double pipValuePerLot = CalculatePipValuePerLot(symbol);
        logger.LogInformation("IndicatorEngine: Calculated Pip Value per Lot for {Symbol}: {PipValue:C}",
            symbol.SymbolName, pipValuePerLot);

        double stopLossPips = stopLossDistance / symbol.PipSize;
        if (stopLossPips <= 0)
        {
            return new TradeDecision {Note = "Invalid StopLoss distance."};
        }

        double riskAmount = Math.Min(account.Equity * RiskPercentage, RiskPerTradeUSD);
        double positionSizeLots = riskAmount / (stopLossPips * pipValuePerLot);
        positionSizeLots = Math.Round(positionSizeLots / symbol.StepVolume) * symbol.StepVolume;

        if (positionSizeLots < symbol.StepVolume)
        {
            return new TradeDecision {Note = "Calculated position size is too small."};
        }

        return new TradeDecision
        {
            Action = signal,
            EntryPrice = (signal == ActionKind.Buy) ? marketEntry.Ask : marketEntry.Bid,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = 0,
            PositionSizeLots = positionSizeLots,
            Note = "Indicator-based signal confirmed."
        };
    }

    private double CalculatePipValuePerLot(SymbolDetails details)
    {
        return (details.PipSize / details.QuoteToAccountRate) * details.LotSize;
    }

    private ActionKind GetTradingSignal(MarketData phase, MarketData pattern, MarketData entry)
    {
        logger.LogInformation("--- Evaluating Indicator-Based Strategy Conditions ---");

        var adxThreshold = 15.0;
        if (pattern.Adx < adxThreshold)
        {
            logger.LogWarning("IndicatorEngine: Trend is too weak (ADX < {Threshold}). No trade allowed.",
                adxThreshold);
            return ActionKind.Hold;
        }

        logger.LogInformation("IndicatorEngine: ADX Check Passed (ADX: {AdxValue} >= {Threshold})", pattern.Adx,
            adxThreshold);

        var isPhaseUpTrend = phase.Close > phase.EmaSlow;
        var isPatternUpTrend = pattern.Close > pattern.EmaSlow;
        logger.LogInformation("IndicatorEngine: Phase (H1) Trend: IsUpTrend = {IsUpTrend}", isPhaseUpTrend);
        logger.LogInformation("IndicatorEngine: Pattern (M15) Trend: IsUpTrend = {IsUpTrend}", isPatternUpTrend);

        var atrMultiplier = 1.5;
        var isVolatile = entry.Atr > (entry.AtrSma * atrMultiplier);
        logger.LogInformation(
            "IndicatorEngine: Volatility Check (M5): Is Volatile = {IsVolatile} (ATR: {Atr}, Threshold: {Threshold})",
            isVolatile, entry.Atr, entry.AtrSma * atrMultiplier);

        bool entryBuyTrigger;
        bool entrySellTrigger;

        if (isVolatile)
        {
            logger.LogInformation("IndicatorEngine: Entry Mode: Momentum (Aggressive)");
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
            "IndicatorEngine: Entry (M5) Trigger Check: BuyTrigger = {BuyTrigger}, SellTrigger = {SellTrigger}",
            entryBuyTrigger, entrySellTrigger);

        if (isPhaseUpTrend && isPatternUpTrend && entryBuyTrigger)
        {
            logger.LogInformation("✅ IndicatorEngine: SUCCESS: All conditions for a BUY signal are met.");
            return ActionKind.Buy;
        }

        if (!isPhaseUpTrend && !isPatternUpTrend && entrySellTrigger)
        {
            logger.LogInformation("✅ IndicatorEngine: SUCCESS: All conditions for a SELL signal are met.");
            return ActionKind.Sell;
        }

        return ActionKind.Hold;
    }
}