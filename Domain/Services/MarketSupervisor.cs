using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Domain.Enum;

namespace Domain.Services;

public class MarketSupervisor(
    IndicatorBasedEngine indicatorEngine,
    PriceActionEngine priceActionEngine,
    ILogger<MarketSupervisor> logger)
{
    public IStrategyEngine SelectStrategy(MarketData patternData, List<PriceZone> zones)
    {
        logger.LogInformation("Supervisor selecting strategy...");
        const double adxThreshold = 25.0;
        
        if (patternData.Adx < adxThreshold || zones.Count > 0)
        {
            logger.LogInformation("Strategy selected: PriceActionEngine (Range or Key Levels detected).");
            return priceActionEngine;
        }
        else 
        {
            logger.LogInformation("Strategy selected: IndicatorBasedEngine (Strong Trend detected).");
            return indicatorEngine;
        }
    }
}