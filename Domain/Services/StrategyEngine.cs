namespace Domain.Services;
using Domain.Enum;

public class StrategyEngine
{
    private const double RiskPerTradeUSD = 1.0;
    private const double RewardRatio = 5.0;
    private const double RiskPercentage = 0.01;

    public TradeDecision Evaluate(AccountInfo account, SymbolInfo symbol, MarketData market)
    {
        var signal = GetTradingSignal(market);
        if (signal == ActionKind.Hold) return new TradeDecision();

        double stopLossDistance = market.Atr * 1.5;
        double stopLossPrice = (signal == ActionKind.Buy) ? market.Ask - stopLossDistance : market.Bid + stopLossDistance;

        double equityRisk = account.Equity * RiskPercentage;
        double riskAmount = Math.Min(equityRisk, RiskPerTradeUSD);

        double pipValuePerLot = 10.0; // این مقدار باید دقیق‌تر محاسبه یا از API خوانده شود
        double stopLossPips = stopLossDistance / symbol.PipSize;
        if (stopLossPips == 0) return new TradeDecision { Note = "Invalid StopLoss distance (zero pips)." };

        double positionSizeLots = riskAmount / (stopLossPips * pipValuePerLot);
        positionSizeLots = Math.Round(positionSizeLots / symbol.StepVolume) * symbol.StepVolume;
        if (positionSizeLots < symbol.StepVolume) return new TradeDecision { Note = "Calculated position size is too small." };

        double takeProfitDistance = stopLossDistance * RewardRatio;
        double takeProfitPrice = (signal == ActionKind.Buy) ? market.Ask + takeProfitDistance : market.Bid - takeProfitDistance;

        return new TradeDecision
        {
            Action = signal,
            EntryPrice = (signal == ActionKind.Buy) ? market.Ask : market.Bid,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            PositionSizeLots = positionSizeLots,
            Note = "Signal confirmed, risk calculated."
        };
    }

    private ActionKind GetTradingSignal(MarketData market)
    {
        if (market.Close > market.Open) return ActionKind.Buy;
        if (market.Close < market.Open) return ActionKind.Sell;
        return ActionKind.Hold;
    }
}