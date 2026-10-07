using Domain.Enum;
using Domain.Parameters;

namespace Domain.Functions;

public static class IndicatorCalculator
{
    public static MarketData Calculate(IReadOnlyList<MarketData> chronological, StrategyParameters parameters,
        double bid, double ask, int quoteAgeSeconds = 0)
    {
        if (parameters.Validate() is { } error) throw new ArgumentException(error);
        ValidateCandles(chronological);
        if (chronological.Count < parameters.WarmupBars || !double.IsFinite(bid) || !double.IsFinite(ask) ||
            bid <= 0 || ask < bid || quoteAgeSeconds < 0) throw new ArgumentException("Indicator warmup or quote is invalid");
        var candles = chronological.TakeLast(parameters.HistoryBars).ToArray();
        var fast = Ema(candles, parameters.FastEmaPeriod);
        var slow = Ema(candles, parameters.SlowEmaPeriod);
        double atr = 0, plus = 0, minus = 0, adx = 0, adxSeed = 0;
        var dxCount = 0;
        var atrValues = new List<double>();
        for (var i = 1; i < candles.Length; i++)
        {
            var current = candles[i]; var previous = candles[i - 1];
            var tr = Math.Max(current.High - current.Low, Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));
            atr = i <= parameters.AtrPeriod ? atr + tr / parameters.AtrPeriod :
                (atr * (parameters.AtrPeriod - 1) + tr) / parameters.AtrPeriod;
            if (i >= parameters.AtrPeriod) atrValues.Add(atr);
            var up = current.High - previous.High; var down = previous.Low - current.Low;
            var plusDm = up > down && up > 0 ? up : 0;
            var minusDm = down > up && down > 0 ? down : 0;
            var period = parameters.AdxPeriod;
            plus = i <= period ? plus + plusDm : plus - plus / period + plusDm;
            minus = i <= period ? minus + minusDm : minus - minus / period + minusDm;
            if (i < period) continue;
            var dx = plus + minus == 0 ? 0 : 100 * Math.Abs(plus - minus) / (plus + minus);
            dxCount++;
            if (dxCount <= period)
            {
                adxSeed += dx;
                if (dxCount == period) adx = adxSeed / period;
            }
            else adx = (adx * (period - 1) + dx) / period;
        }
        var averageAtr = atrValues.TakeLast(parameters.AtrAveragePeriod).Average();
        if (!double.IsFinite(fast) || !double.IsFinite(slow) || !double.IsFinite(atr) ||
            !double.IsFinite(averageAtr) || !double.IsFinite(adx))
            throw new ArgumentException("Indicator calculations exceed the supported numeric range");
        return candles[^1] with { EmaFast = fast, EmaSlow = slow, Atr = atr,
            AtrSma = averageAtr, Adx = adx,
            Bid = bid, Ask = ask, QuoteAgeSeconds = quoteAgeSeconds };
    }

    public static void ValidateCandles(IReadOnlyList<MarketData> candles)
    {
        if (candles is null || candles.Count == 0) throw new ArgumentException("No candles supplied");
        long previousTime = 0;
        foreach (var c in candles)
        {
            if (c is null || c.OpenTime <= previousTime || !double.IsFinite(c.Open) || !double.IsFinite(c.Close) ||
                !double.IsFinite(c.High) || !double.IsFinite(c.Low) || c.Low <= 0 ||
                c.High < Math.Max(c.Open, c.Close) || c.Low > Math.Min(c.Open, c.Close))
                throw new ArgumentException("Candles must contain finite OHLC values and unique increasing timestamps");
            previousTime = c.OpenTime;
        }
    }

    private static double Ema(IReadOnlyList<MarketData> candles, int period)
    {
        var value = candles.Take(period).Average(c => c.Close);
        var alpha = 2.0 / (period + 1);
        for (var i = period; i < candles.Count; i++) value += alpha * (candles[i].Close - value);
        return value;
    }
}
