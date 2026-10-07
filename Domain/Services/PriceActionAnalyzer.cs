using Microsoft.Extensions.Logging;

using Domain.Enum;

namespace Domain.Services;

public  class PriceActionAnalyzer(ILogger<PriceActionAnalyzer> logger)
{
    public List<PriceZone> DetectZones(List<MarketData> candles, double mergeThreshold, int minimumStrength = 2)
    {
        var fractalPrices = FindFractalPrices(candles);
        var zones = ClusterFractalsIntoZones(fractalPrices, mergeThreshold);
        logger.LogInformation("PriceActionAnalyzer: Detected {ZoneCount} raw zones from {FractalCount} fractals.", zones.Count, fractalPrices.Count);
        
        var strongZones = zones.Where(z => z.Strength >= minimumStrength).ToList();
        logger.LogInformation("PriceActionAnalyzer: Filtered down to {StrongZoneCount} strong zones (strength > 1).", strongZones.Count);
        
        return strongZones;
    }

    private List<double> FindFractalPrices(List<MarketData> candles)
    {
        var prices = new List<double>();
        if (candles.Count < 5) return prices;

        for (int i = 2; i < candles.Count - 2; i++)
        {
            var m = candles[i];
            var p1 = candles[i - 1];
            var p2 = candles[i - 2];
            var n1 = candles[i + 1];
            var n2 = candles[i + 2];

            bool isHighFractal = m.High > p1.High && m.High > p2.High && m.High > n1.High && m.High > n2.High;
            if (isHighFractal)
                prices.Add(m.High);

            bool isLowFractal = m.Low < p1.Low && m.Low < p2.Low && m.Low < n1.Low && m.Low < n2.Low;
            if (isLowFractal)
                prices.Add(m.Low);
        }
        return prices;
    }

    private List<PriceZone> ClusterFractalsIntoZones(List<double> prices, double mergeThreshold)
    {
        if (prices.Count == 0) return new List<PriceZone>();
        
        prices.Sort();
        
        var zones = new List<PriceZone>();
        if(prices.Count == 0) return zones;

        var currentZone = new PriceZone { Top = prices[0], Bottom = prices[0], Strength = 1 };
        zones.Add(currentZone);

        for (int i = 1; i < prices.Count; i++)
        {
            if (System.Math.Abs(prices[i] - currentZone.Bottom) <= mergeThreshold)
            {
                currentZone.Top = prices[i];
                currentZone.Strength++;
            }
            else
            {
                currentZone = new PriceZone { Top = prices[i], Bottom = prices[i], Strength = 1 };
                zones.Add(currentZone);
            }
        }
        return zones;
    }
}
