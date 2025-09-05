using Domain.Enum;

namespace Domain.Services;

public class StrategyEngine
{
  
    private const double RiskPerTradeUSD = 20.0;
    private const double RewardRatio = 2.0; 
    private const double RiskPercentage = 0.01;
    private const int TrendFilterPeriod = 20; 
    private const int RsiPeriod = 14; 

    public TradeDecision Evaluate(AccountInfo account, SymbolInfo symbol, MarketData market, List<double> recentCloses)
    {
        var sma = CalculateSMA(recentCloses, TrendFilterPeriod);
        var rsi = CalculateRSI(recentCloses, RsiPeriod);
        
        var signal = GetTradingSignal(market, sma, rsi);
        if (signal == ActionKind.Hold)
        {
            return new TradeDecision { Note = "No valid signal based on new strategy (SMA/RSI)." };
        }
        
        double stopLossDistance = market.Atr * 1.5;
        double stopLossPrice = (signal == ActionKind.Buy) ? market.Ask - stopLossDistance : market.Bid + stopLossDistance;

        double equityRisk = account.Equity * RiskPercentage;
        double riskAmount = Math.Min(equityRisk, RiskPerTradeUSD);

        double pipValuePerLot = 10.0;
        double stopLossPips = stopLossDistance / symbol.PipSize;
        if (stopLossPips <= 0)
        {
            return new TradeDecision { Note = "Invalid StopLoss distance (zero or negative pips)." };
        }

        double positionSizeLots = riskAmount / (stopLossPips * pipValuePerLot);
        
        
        positionSizeLots = Math.Round(positionSizeLots / symbol.StepVolume) * symbol.StepVolume;
        
        if (positionSizeLots < symbol.StepVolume)
        {
            return new TradeDecision { Note = "Calculated position size is too small." };
        }

        double takeProfitDistance = stopLossDistance * RewardRatio;
        double takeProfitPrice = (signal == ActionKind.Buy) ? market.Ask + takeProfitDistance : market.Bid - takeProfitDistance;

        return new TradeDecision
        {
            Action = signal,
            EntryPrice = (signal == ActionKind.Buy) ? market.Ask : market.Bid,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            PositionSizeLots = positionSizeLots,
            Note = "Signal confirmed by SMA/RSI, risk calculated."
        };
    }

    private ActionKind GetTradingSignal(MarketData market, double smaValue, double rsiValue)
    {
        bool isUpTrend = market.Close > smaValue;
        bool isDownTrend = market.Close < smaValue;
        
        bool isBullishCandle = market.Close > market.Open;
        bool isBearishCandle = market.Close < market.Open;
        
        if (isUpTrend && isBullishCandle && rsiValue > 50)
            return ActionKind.Buy;
        
        if (isDownTrend && isBearishCandle && rsiValue < 50)
            return ActionKind.Sell;

        return ActionKind.Hold;
    }


    private static double CalculateSMA(List<double> prices, int period)
    {
        if (prices.Count < period) return 0;
        return prices.Take(period).Average();
    }
    
    private static double CalculateRSI(List<double> prices, int period)
    {
        if (prices.Count < period + 1) return 50;

        double sumGains = 0;
        double sumLosses = 0;
        
        var orderedPrices = prices.AsEnumerable().Reverse().ToList();
        
        for (int i = orderedPrices.Count - period; i < orderedPrices.Count; i++)
        {
            var difference = orderedPrices[i] - orderedPrices[i - 1];
            if (difference >= 0)
            {
                sumGains += difference;
            }
            else
            {
                sumLosses += Math.Abs(difference);
            }
        }

        if (sumGains == 0) return 0;
        if (sumLosses == 0) return 100;

        var averageGain = sumGains / period;
        var averageLoss = sumLosses / period;
        
        var rs = averageGain / averageLoss;
        var rsi = 100 - (100 / (1 + rs));

        return rsi;
    }
}