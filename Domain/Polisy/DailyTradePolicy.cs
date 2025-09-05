
namespace Domain.Polisy;

public class DailyTradePolicy
{
    private static readonly List<DateTime> TradesToday = [];
    private const int MaxTradesPerDay = 5;

    public bool CanExecuteTrade()
    {
        TradesToday.RemoveAll(tradeTime => tradeTime.Date < DateTime.UtcNow.Date);

        return TradesToday.Count < MaxTradesPerDay;
    }

    public void RegisterTrade()
    {
        TradesToday.Add(DateTime.UtcNow);
    }
}