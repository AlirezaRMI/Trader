using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Domain.Enum;

namespace Domain.Services;

using Domain.Parameters;

public class MarketSupervisor(
    IndicatorBasedEngine indicatorEngine,
    PriceActionEngine priceActionEngine,
    ILogger<MarketSupervisor> logger)
{
    public IStrategyEngine SelectStrategy(MarketData patternData, List<PriceZone> zones, StrategyParameters? parameters = null)
    {
        parameters ??= new();
        if (parameters.Mode == "Indicator") return indicatorEngine;
        if (parameters.Mode == "PriceAction") return priceActionEngine;
        logger.LogInformation("Supervisor selecting strategy based on Market Conditions...");
        var strongTrendAdx = parameters.AutoSwitchAdx;
        var weakTrendAdx = parameters.WeakTrendAdx;

        if (patternData.Adx >= strongTrendAdx)
        {
            logger.LogInformation("Strategy selected: IndicatorBasedEngine (Reason: Very Strong Trend, ADX > {threshold}).", strongTrendAdx);
            return indicatorEngine;
        }
        if (zones.Count > 0 || patternData.Adx < weakTrendAdx)
        {
            logger.LogInformation("Strategy selected: PriceActionEngine (Reason: Key Levels detected or Weak Trend).");
            return priceActionEngine;
        }
        logger.LogInformation("Strategy selected: IndicatorBasedEngine (Reason: Default for moderate trend).");
        return indicatorEngine;
    }
}
