using Domain.Enum;

namespace Domain.Polisy;

public class DailyTradePolicy
{
    private static readonly List<DateTime> TradesToday = new();
    private const int MaxTradesPerDay = 10;

    public bool IsAllowed(ActionKind action, double equity)
    {
        // Clean up old trades from previous days
        TradesToday.RemoveAll(tradeTime => tradeTime.Date < DateTime.UtcNow.Date);

        // Check max trades per day (ignores action and equity for now, but can extend)
        if (TradesToday.Count >= MaxTradesPerDay)
        {
            return false;
        }

        // Optional: Add equity-based check, e.g., if equity < threshold, block
        // if (equity < 1000) return false; // Example

        return true;
    }

    public void RegisterTrade(ActionKind action)
    {
        TradesToday.Add(DateTime.UtcNow);
    }
}