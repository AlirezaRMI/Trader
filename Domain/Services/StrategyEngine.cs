using Domain.Enum;
using Domain.Polisy;
using Domain.Trading;

namespace Domain.Services;

public sealed class StrategyEngine
{
    public Decision Evaluate(Symbol symbol, Timeframe tf, double bid, double ask, Atr atr, double close,
        DailyTradePolicy gate, ISeriesReader series)
    {
        if (!gate.CanTrade()) return new Decision(ActionKind.Hold, new Lots(0), Note: "daily cap");
        if (atr.Value <= 0 || ask - bid <= 0) return new Decision(ActionKind.Hold, new Lots(0), Note: "bad market");

        series.AppendClose(symbol, tf, close);
        var closes = series.GetCloses(symbol, tf, 50);
        if (closes.Count < 20) return new Decision(ActionKind.Hold, new Lots(0), Note: "warmup");

        double Sma(int n) => closes.TakeLast(n).Average();
        var sma10 = Sma(10);
        var sma20 = Sma(20);

        var action = sma10 > sma20 ? ActionKind.Buy :
            sma10 < sma20 ? ActionKind.Sell : ActionKind.Hold;

        if (action == ActionKind.Hold) return new Decision(ActionKind.Hold, new Lots(0), Note: "flat");

        var riskPerTrade = 0.01; // 1% مثال
        var price = (action == ActionKind.Buy) ? ask : bid;
        var stop  = atr.Value * 1.5;
        var size  = Math.Max(0.01, Math.Round(riskPerTrade / (stop + 1e-6), 2)); // lots ساده

        double sl, tp;
        if (action == ActionKind.Buy)
        {
            sl = bid - 1.5 * atr.Value;
            tp = bid + 2.0 * atr.Value;
        }
        else
        {
            sl = ask + 1.5 * atr.Value;
            tp = ask - 2.0 * atr.Value;
        }
        return new Decision(action, new Lots(size), sl, tp, "SMAxATR");
    }
}