using Domain.Enum;
namespace Domain.Services;

public class StrategyEngine
{
    private const double RiskPerTradeUSD = 10.0;
    private const double RiskPercentage = 0.01;
    
    public TradeDecision Evaluate(AccountInfo account, SymbolInfo symbol, MarketData market)
    {
        var signal = GetTradingSignal(market);
        if (signal == ActionKind.Hold)
        {
            return new TradeDecision { Note = "No pullback signal detected." };
        }
        
        double stopLossPrice;
        if (signal == ActionKind.Buy)
        {
            stopLossPrice = market.Low - (symbol.PipSize * 2);
        }
        else
        {
            stopLossPrice = market.High + (symbol.PipSize * 2); 
        }
        
        double stopLossDistance = Math.Abs(market.Close - stopLossPrice);
        double pipValuePerLot = 10.0;
        double stopLossPips = stopLossDistance / symbol.PipSize;
        if (stopLossPips <= 0)
        {
            return new TradeDecision { Note = "Invalid StopLoss distance." };
        }

        double riskAmount = Math.Min(account.Equity * RiskPercentage, RiskPerTradeUSD);
        double positionSizeLots = riskAmount / (stopLossPips * pipValuePerLot);
        positionSizeLots = Math.Round(positionSizeLots / symbol.StepVolume) * symbol.StepVolume;
        
        if (positionSizeLots < symbol.StepVolume)
        {
            return new TradeDecision { Note = "Calculated position size is too small." };
        }

        return new TradeDecision
        {
            Action = signal,
            EntryPrice = (signal == ActionKind.Buy) ? market.Ask : market.Bid,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = 0,
            PositionSizeLots = positionSizeLots,
            Note = "Pullback signal confirmed."
        };
    }
    
    private ActionKind GetTradingSignal(MarketData market)
    {
        bool isUpTrend = market.Close > market.EmaSlow;
        bool isDownTrend = market.Close < market.EmaSlow;
        
        bool isBullishCandle = market.Close > market.Open;
        bool isBearishCandle = market.Close < market.Open;
        if (isUpTrend && market.Low <= market.EmaFast && isBullishCandle)
        {
            return ActionKind.Buy;
        }
        
        if (isDownTrend && market.High >= market.EmaFast && isBearishCandle)
        {
            return ActionKind.Sell;
        }

        return ActionKind.Hold;
    }
}