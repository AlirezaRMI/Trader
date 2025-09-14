namespace Domain.Services.Interfaces;

public interface IEconomicCalendarService
{
    Task RefreshCalendarAsync();
    bool IsInEmbargoPeriod(string currencyPair, int minutesBefore, int minutesAfter);
}