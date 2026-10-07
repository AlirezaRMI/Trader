using Domain.Enum;

namespace Domain.Functions;

public static class CandleAggregator
{
    public static IReadOnlyList<MarketData> Aggregate(IReadOnlyList<MarketData> chronological, int sourceMinutes, int targetMinutes)
    {
        if (sourceMinutes <= 0 || targetMinutes < sourceMinutes || targetMinutes % sourceMinutes != 0)
            throw new ArgumentException("Timeframe must be a multiple of the source interval");
        var interval = (long)targetMinutes * 60; var step = sourceMinutes * 60;
        var result = new List<MarketData>();
        foreach (var group in chronological.GroupBy(c => c.OpenTime / interval * interval))
        {
            var bars = group.ToArray();
            if (bars.Length != targetMinutes / sourceMinutes || bars[0].OpenTime != group.Key ||
                bars.Where((c, i) => c.OpenTime != group.Key + i * step).Any()) continue;
            result.Add(new() { OpenTime = group.Key, Open = bars[0].Open, Close = bars[^1].Close,
                High = bars.Max(c => c.High), Low = bars.Min(c => c.Low) });
        }
        return result;
    }
}
