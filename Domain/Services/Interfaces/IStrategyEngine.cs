using Domain.Enum;

namespace Domain.Services.Interfaces;

public interface IStrategyEngine
{
    TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, 
        MarketData marketPhase, MarketData marketPattern, MarketData marketEntry, 
        long lastTradeSignalTime, List<PriceZone> zones, List<MarketData> history);
}