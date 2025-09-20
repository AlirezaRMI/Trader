using Domain.Enum;
using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;


namespace Domain.Services;

public class PriceActionEngine(ILogger<PriceActionEngine> logger) : IStrategyEngine
{
    private static readonly Dictionary<double, DateTime> BrokenSupportZones = new();
    private static readonly Dictionary<double, DateTime> BrokenResistanceZones = new();

    private const double RiskPerTradeUSD = 10.0;
    private const double RiskPercentage = 0.01;

    public TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, MarketData marketPhase, MarketData marketPattern, MarketData marketEntry, long lastTradeSignalTime, List<PriceZone> zones, List<MarketData> history)
    {
        if (history.Count < 2) return new TradeDecision { Note = "Not enough history for PA engine."};
        if (marketEntry.OpenTime == lastTradeSignalTime)
        {
             logger.LogWarning("PriceActionEngine: Signal on current candle ({OpenTime}) has already been traded. Skipping.", marketEntry.OpenTime);
            return new TradeDecision { Note = "Signal already traded on this candle." };
        }
        
        var currentCandle = history[0];
        var previousCandle = history[1];

        CleanUpOldBrokenZones(TimeSpan.FromHours(4));
        DetectBreaks(currentCandle, previousCandle, zones);
        var signal = CheckForRetestSignal(currentCandle);

        if(signal == ActionKind.Hold)
        {
            return new TradeDecision { Note = "No PA signal."};
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
        double pipValuePerLot = (symbol.PipSize / symbol.QuoteToAccountRate) * symbol.LotSize;
        double stopLossPips = stopLossDistance / symbol.PipSize;

        if (stopLossPips <= 0) return new TradeDecision { Note = "PA: Invalid SL distance."};

        double riskAmount = Math.Min(account.Equity * RiskPercentage, RiskPerTradeUSD);
        double positionSizeLots = riskAmount / (stopLossPips * pipValuePerLot);
        positionSizeLots = Math.Round(positionSizeLots / symbol.StepVolume) * symbol.StepVolume;

        if (positionSizeLots < symbol.StepVolume)
        {
            return new TradeDecision { Note = "PA: Position size too small."};
        }

        return new TradeDecision
        {
            Action = signal,
            EntryPrice = (signal == ActionKind.Buy) ? currentCandle.Ask : currentCandle.Bid,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = 0,
            PositionSizeLots = positionSizeLots,
            Note = "Price Action signal confirmed."
        };
    }

    private void DetectBreaks(MarketData current, MarketData previous, List<PriceZone> zones)
    {
        var supportZones = zones.Where(z => z.Top < current.Close).ToList();
        var resistanceZones = zones.Where(z => z.Bottom > current.Close).ToList();

        foreach (var zone in supportZones)
        {
            if (previous.Close > zone.Top && current.Close < zone.Bottom && !BrokenSupportZones.ContainsKey(zone.Bottom))
            {
                logger.LogWarning("PA: Support Zone broken at {Price}", zone.Bottom);
                BrokenSupportZones[zone.Bottom] = DateTime.UtcNow;
            }
        }
        
        foreach (var zone in resistanceZones)
        {
            if (previous.Close < zone.Bottom && current.Close > zone.Top && !BrokenResistanceZones.ContainsKey(zone.Top))
            {
                logger.LogWarning("PA: Resistance Zone broken at {Price}", zone.Top);
                BrokenResistanceZones[zone.Top] = DateTime.UtcNow;
            }
        }
    }
    
    private ActionKind CheckForRetestSignal(MarketData current)
    {
        foreach (var zonePrice in BrokenSupportZones.Keys.ToList())
        {
            if (current.High >= zonePrice && current.Close < zonePrice)
            {
                logger.LogInformation("✅ PA Sell Signal: Retest of broken support at {Price}", zonePrice);
                BrokenSupportZones.Remove(zonePrice);
                return ActionKind.Sell;
            }
        }
        
        foreach (var zonePrice in BrokenResistanceZones.Keys.ToList())
        {
            if (current.Low <= zonePrice && current.Close > zonePrice)
            {
                logger.LogInformation("✅ PA Buy Signal: Retest of broken resistance at {Price}", zonePrice);
                BrokenResistanceZones.Remove(zonePrice);
                return ActionKind.Buy;
            }
        }
        return ActionKind.Hold;
    }

    private void CleanUpOldBrokenZones(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        var supportKeysToRemove = BrokenSupportZones.Where(kvp => now - kvp.Value > maxAge).Select(kvp => kvp.Key).ToList();
        foreach (var key in supportKeysToRemove) BrokenSupportZones.Remove(key);
        
        var resistanceKeysToRemove = BrokenResistanceZones.Where(kvp => now - kvp.Value > maxAge).Select(kvp => kvp.Key).ToList();
        foreach (var key in resistanceKeysToRemove) BrokenResistanceZones.Remove(key);
    }
}