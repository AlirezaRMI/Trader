using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Domain.Enum;
using Domain.Services.Interfaces;
using System.Globalization;

namespace Domain.Services;

public class EconomicCalendarService(ILogger<EconomicCalendarService> logger) : IEconomicCalendarService
{
    private static List<EconomicEvent> _calendarCache = new();
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

        logger.LogInformation("Refreshing economic calendar from Investing.com using Playwright...");

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var page = await browser.NewPageAsync();
            
            await page.GotoAsync("https://www.investing.com/economic-calendar/");
            
            await page.WaitForSelectorAsync("#economicCalendarData");

            var html = await page.ContentAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var newEvents = new List<EconomicEvent>();

            var rows = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'js-event-item')]");

            if (rows == null)
            {
                logger.LogWarning("Could not find calendar rows on Investing.com.");
                return;
            }

            foreach (var row in rows)
            {
                var sentimentNode = row.SelectSingleNode(".//td[contains(@class, 'sentiment')]");
                var volatilityTitle = sentimentNode?.Attributes["data-img_key"]?.Value;
                
                if (volatilityTitle == "bull3")
                {
                    var timeNode = row.SelectSingleNode(".//td[contains(@class, 'time')]");
                    var currencyNode = row.SelectSingleNode(".//td[contains(@class, 'flagCur')]");
                    var eventNameNode = row.SelectSingleNode(".//td[contains(@class, 'event')]//a");

                    if (timeNode != null && currencyNode != null && eventNameNode != null)
                    {
                        var timeText = timeNode.InnerText.Trim();
                        var currencyText = currencyNode.InnerText.Trim();
                        var eventNameText = eventNameNode.InnerText.Trim();

                        try
                        {
                            var timeSpan = TimeSpan.Parse(timeText, CultureInfo.InvariantCulture);
                            var eventTime = DateTime.UtcNow.Date.Add(timeSpan);

                            newEvents.Add(new EconomicEvent
                            {
                                EventTime = eventTime,
                                Currency = currencyText,
                                Impact = "High",
                                EventName = eventNameText
                            });
                        }
                        catch (FormatException)
                        {
                            // Ignore rows that don't have a valid time (e.g., "All Day")
                        }
                    }
                }
            }

            _calendarCache = newEvents;
            _lastCacheRefresh = DateTime.UtcNow;
            logger.LogInformation(
                "Successfully refreshed calendar from Investing.com. Found {Count} high-impact events.",
                newEvents.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh economic calendar from Investing.com.");
        }
    }
}