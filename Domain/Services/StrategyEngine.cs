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

        double sma(int n) => closes.TakeLast(n).Average();
        var fast = sma(10);
        var slow = sma(20);

        var action = fast > slow ? ActionKind.Buy : fast < slow ? ActionKind.Sell : ActionKind.Hold;
        if (action == ActionKind.Hold) return new Decision(ActionKind.Hold, new Lots(0.0), Note: "no edge");

        var size = new Lots(0.10);
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

        gate.RegisterTrade();
        return new Decision(action, size, sl, tp, "SMAxATR");
    }
}