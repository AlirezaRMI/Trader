// In Domain.Policy
namespace Domain.Polisy;

public class DailyTradePolicy
{
    private static readonly List<DateTime> _tradesToday = new();
    private const int MaxTradesPerDay = 5;

    public bool CanExecuteTrade()
    {
        _tradesToday.RemoveAll(tradeTime => tradeTime.Date < DateTime.UtcNow.Date);

        if (_tradesToday.Count >= MaxTradesPerDay)
        {
            return false; 
        }
        return true;
    }

    public void RegisterTrade()
    {
        _tradesToday.Add(DateTime.UtcNow);
    }
}