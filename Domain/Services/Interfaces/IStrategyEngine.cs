using Domain.Enum;

namespace Domain.Services.Interfaces;

using Domain.Parameters;

public interface IStrategyEngine
{
    TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, 
        MarketData marketPhase, MarketData marketPattern, MarketData marketEntry, 
        long lastTradeSignalTime, List<PriceZone> zones, List<MarketData> history,
        StrategyParameters? parameters = null, double riskPercent = 1, double maximumRiskAmount = 5);
}
