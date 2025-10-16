using Microsoft.Extensions.Logging;
using System.Globalization;
using Newtonsoft.Json;
using Domain.Enum;
using Domain.Services.Interfaces;

namespace Domain.Services
{
    public class EconomicCalendarService(
        ILogger<EconomicCalendarService> logger,
        HttpClient httpClient) : IEconomicCalendarService
    {
        private static List<EconomicEvent> _calendarCache = [];
        private static DateTime _lastCacheRefresh = DateTime.MinValue;

        public bool IsInEmbargoPeriod(string currencyPair, int minutesBefore, int minutesAfter)
        {
            var now = DateTime.UtcNow;
            var currency1 = currencyPair.Substring(0, 3);
            var currency2 = currencyPair.Substring(3, 3);

            var highImpactEvents = _calendarCache
                .Where(e => e.Impact == "High" && (e.Currency == currency1 || e.Currency == currency2))
                .ToList();

            foreach (var ev in highImpactEvents)
            {
                var embargoStartTime = ev.EventTime.AddMinutes(-minutesBefore);
                var embargoEndTime = ev.EventTime.AddMinutes(minutesAfter);

                if (now >= embargoStartTime && now <= embargoEndTime)
                {
                    logger.LogWarning(
                        "Trading embargo active due to high-impact event: {EventName} for {Currency} at {EventTime}",
                        ev.EventName, ev.Currency, ev.EventTime);
                    return true;
                }
            }

            return false;
        }

        public async Task RefreshCalendarAsync()
        {
            if (DateTime.UtcNow.Date <= _lastCacheRefresh.Date)
            {
                return;
            }

            logger.LogInformation("Refreshing economic calendar from JBlanked API...");

            try
            {
                var fromDate = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
                var toDate = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
                var url = $"https://www.jblanked.com/news/api/mql5/calendar/range/?from={fromDate}&to={toDate}"; // بدون key

                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogError("JBlanked error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                var newEvents = JsonConvert.DeserializeObject<List<JBlankedEvent>>(content) ?? [];

                var events = newEvents
                    .Where(e => e.Impact == "high") // فقط high impact
                    .Select(e => new EconomicEvent
                    {
                        EventTime = DateTime.ParseExact(e.Date + " " + e.Time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        Currency = e.Currency,
                        Impact = "High",
                        EventName = e.Event
                    }).ToList();

                _calendarCache = events;
                _lastCacheRefresh = DateTime.UtcNow;
                logger.LogInformation("Successfully refreshed calendar from JBlanked. Found {Count} high-impact events.", events.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh economic calendar from JBlanked.");
            }
        }
    }

    public class JBlankedEvent
    {
        public string Event { get; set; } = "";
        public string Currency { get; set; } = "";
        public string Impact { get; set; } = "";
        public string Date { get; set; } = "";
        public string Time { get; set; } = "";
    }
}