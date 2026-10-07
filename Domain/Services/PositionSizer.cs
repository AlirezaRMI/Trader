using Domain.Enum;
using Domain.Parameters;

namespace Domain.Services;

/// <summary>Calculates volume from the real quote-to-stop loss in account currency.</summary>
public static class PositionSizer
{
    public static double CalculateLots(double equity, SymbolDetails symbol, double entryPrice,
        double stopPrice, double riskPercentage = 0.01, double maximumRiskAmount = 5,
        double commissionPerLot = 0, double slippageBufferPoints = 0)
    {
        if (!Positive(equity) || !Positive(entryPrice) || !Positive(stopPrice) ||
            !Positive(riskPercentage) || riskPercentage > 1 || !Positive(maximumRiskAmount) ||
            !Positive(symbol.StepVolume)) return 0;

        var lossPerLot = RiskPerLot(symbol, entryPrice, stopPrice, commissionPerLot, slippageBufferPoints);
        if (!Positive(lossPerLot)) return 0;
        var budget = Math.Min(equity * riskPercentage, maximumRiskAmount);
        var rawVolume = budget / lossPerLot;
        var minimum = Math.Max(symbol.MinVolume, symbol.StepVolume);
        var maximum = symbol.MaxVolume;
        if (!Positive(rawVolume) || !Positive(minimum) || !Positive(maximum) || maximum < minimum) return 0;
        try
        {
            var step = (decimal)symbol.StepVolume;
            if (step <= 0) return 0;
            var volume = (double)(decimal.Floor((decimal)Math.Min(rawVolume, maximum) / step) * step);
            if (volume < minimum || !Positive(volume)) return 0;
            return volume * lossPerLot <= budget + 0.00000001 ? volume : 0;
        }
        catch (OverflowException) { return 0; }
    }

    public static double RiskPerLot(SymbolDetails symbol, double entryPrice, double stopPrice,
        double commissionPerLot = 0, double slippageBufferPoints = 0)
    {
        if (!Positive(symbol.TickSize) || !Positive(symbol.TickValue) || !Positive(symbol.Point) ||
            !Positive(entryPrice) || !Positive(stopPrice) || !Nonnegative(commissionPerLot) ||
            !Nonnegative(slippageBufferPoints)) return 0;
        var distance = Math.Abs(entryPrice - stopPrice);
        if (!Positive(distance)) return 0;
        return (distance + 2 * slippageBufferPoints * symbol.Point) / symbol.TickSize * symbol.TickValue + commissionPerLot;
    }

    /// <summary>Allocates risk around closed-bar structure, then aligns protection to broker ticks.</summary>
    public static TradeDecision CreateRiskPlan(ActionKind side, double equity, SymbolDetails symbol,
        MarketData entry, IReadOnlyList<MarketData> newestFirst, StrategyParameters parameters,
        double riskPercent, double maximumRiskAmount, string note)
    {
        if (side is not (ActionKind.Buy or ActionKind.Sell) || !Positive(entry.Atr) ||
            !Positive(symbol.TickSize) || !Positive(symbol.TickValue) || !Positive(symbol.Point) ||
            newestFirst.Count < parameters.SwingLookbackBars || !Positive(entry.Bid) || !Positive(entry.Ask) || entry.Ask < entry.Bid)
            return new() { Note = "Incomplete data for a broker-safe risk plan" };
        var buy = side == ActionKind.Buy;
        var price = buy ? entry.Ask : entry.Bid;
        var window = newestFirst.Take(parameters.SwingLookbackBars);
        var structural = buy ? window.Min(c => c.Low) - entry.Atr * parameters.StructuralStopBufferAtr :
            window.Max(c => c.High) + entry.Atr * parameters.StructuralStopBufferAtr;
        var brokerDistance = (Math.Max(1, symbol.StopsLevel) + 1) * symbol.Point + entry.Ask - entry.Bid;
        var distance = Math.Max(Math.Max(buy ? price - structural : structural - price,
            entry.Atr * parameters.StopAtrMultiplier), brokerDistance);
        var unroundedStop = price + (buy ? -distance : distance);
        var stop = (buy ? Math.Floor(unroundedStop / symbol.TickSize) : Math.Ceiling(unroundedStop / symbol.TickSize)) * symbol.TickSize;
        if (!Positive(stop)) return new() { Note = "Protective stop is outside the supported price range" };
        var lossPerLot = RiskPerLot(symbol, price, stop, parameters.CommissionPerLot, parameters.SlippageBufferPoints);
        var costDistance = 2 * parameters.SlippageBufferPoints * symbol.Point + parameters.CommissionPerLot * symbol.TickSize / symbol.TickValue;
        var targetDistance = Math.Max(brokerDistance, Math.Max(entry.Atr * parameters.TargetAtrMultiplier,
            parameters.RewardRiskRatio * Math.Abs(price - stop) + (parameters.RewardRiskRatio + 1) * costDistance));
        var unroundedTarget = price + (buy ? targetDistance : -targetDistance);
        var target = (buy ? Math.Ceiling(unroundedTarget / symbol.TickSize) : Math.Floor(unroundedTarget / symbol.TickSize)) * symbol.TickSize;
        var lots = CalculateLots(equity, symbol, price, stop, riskPercent / 100, maximumRiskAmount,
            parameters.CommissionPerLot, parameters.SlippageBufferPoints);
        if (!Positive(target) || !Positive(lossPerLot) || lots <= 0)
            return new() { Note = "Broker minimum volume exceeds the risk budget or protection is invalid" };
        return new() { Action = side, EntryPrice = price, StopLossPrice = stop, TakeProfitPrice = target,
            PositionSizeLots = lots, Note = note };
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
}
