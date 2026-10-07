using Domain.Enum;
using Domain.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Domain.Services;

using Domain.Parameters;

public class PriceActionEngine(ILogger<PriceActionEngine> logger) : IStrategyEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<(long Account, string Server, string Symbol, double Price, ActionKind Side), long> _broken = new();

    public TradeDecision Evaluate(AccountInfo account, SymbolDetails symbol, MarketData marketPhase,
        MarketData marketPattern, MarketData marketEntry, long lastTradeSignalTime,
        List<PriceZone> zones, List<MarketData> history, StrategyParameters? parameters = null,
        double riskPercent = 1, double maximumRiskAmount = 5)
    {
        parameters ??= new();
        lock (_sync)
        {
            // A signal is consumed by confirmed execution, not by observation while paused.
            foreach (var consumed in _broken.Where(x => x.Key.Account == account.AccountId &&
                         x.Key.Server == account.Server && x.Key.Symbol == symbol.SymbolName && x.Value < lastTradeSignalTime).ToArray())
                _broken.Remove(consumed.Key);
            if (history.Count < 2 || marketEntry.OpenTime == lastTradeSignalTime)
                return new TradeDecision { Note = "Insufficient history or duplicate signal" };

            var current = history[0];
            var previous = history[1];
            foreach (var expired in _broken.Where(x => current.OpenTime - x.Value >
                         (long)parameters.RetestExpiryBars * parameters.EntryTimeframeMinutes * 60).ToArray())
                _broken.Remove(expired.Key);

            foreach (var zone in zones.Where(x => double.IsFinite(x.Bottom) && double.IsFinite(x.Top) && x.Bottom > 0 && x.Top >= x.Bottom))
            {
                var buffer = marketEntry.Atr * parameters.BreakoutBufferAtr;
                if (previous.Close >= zone.Bottom && current.Close < zone.Bottom - buffer)
                    _broken.TryAdd((account.AccountId, account.Server, symbol.SymbolName, zone.Bottom, ActionKind.Sell), current.OpenTime);
                if (previous.Close <= zone.Top && current.Close > zone.Top + buffer)
                    _broken.TryAdd((account.AccountId, account.Server, symbol.SymbolName, zone.Top, ActionKind.Buy), current.OpenTime);
            }

            foreach (var setup in _broken.Where(x => x.Key.Account == account.AccountId && x.Key.Server == account.Server && x.Key.Symbol == symbol.SymbolName).ToArray())
            {
                if (current.OpenTime <= setup.Value)
                    continue;

                var isBuy = setup.Key.Side == ActionKind.Buy;
                var tolerance = marketEntry.Atr * parameters.RetestToleranceAtr;
                var invalidated = isBuy ? current.Close < setup.Key.Price - tolerance : current.Close > setup.Key.Price + tolerance;
                if (invalidated) { _broken.Remove(setup.Key); continue; }
                var retest = isBuy
                    ? current.Low <= setup.Key.Price + tolerance && current.Close > setup.Key.Price && current.Close > current.Open
                    : current.High >= setup.Key.Price - tolerance && current.Close < setup.Key.Price && current.Close < current.Open;
                if (!retest)
                    continue;

                logger.LogInformation("Confirmed {Side} retest on {Symbol} at {Level}", setup.Key.Side, setup.Key.Symbol, setup.Key.Price);
                return PositionSizer.CreateRiskPlan(setup.Key.Side, account.Equity, symbol, marketEntry, history,
                    parameters, riskPercent, maximumRiskAmount, "Confirmed break and later-candle retest");
            }

            return new TradeDecision { Note = "No confirmed retest" };
        }
    }
}
