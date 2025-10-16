using System.Globalization;
using Domain.Enum;
using Hangfire;
using Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Infrastructure.Extention;

[DisableConcurrentExecution(timeoutInSeconds: 30)]
public class PositionManagementJob(ILogger<PositionManagementJob> logger)
{
    public static readonly List<long> OpenTradeTickets = [];
    public static readonly Dictionary<string, long> LastTradeSignalTimes = new(); 
    public static readonly Dictionary<string, (List<MarketData> History, DateTime FetchedAt)> HistoryCache = new();

    public async Task ManagePositions()
    {
        logger.LogInformation("--- Position Management Cycle Started ---");
        using (var bridge = new MetaTraderPipeClient(logger))
        {
            try
            {
                bridge.Connect();
                
                string tradingSymbol = await bridge.SendCommandAsync("GET_CHART_SYMBOL");
                if (string.IsNullOrEmpty(tradingSymbol) || tradingSymbol.StartsWith("ERROR"))
                {
                    logger.LogError("Could not get the active chart symbol from MT4. Response: {Response}", tradingSymbol);
                    return;
                }
                
                logger.LogInformation(">>>>>> Managing positions for symbol: {Symbol} <<<<<<", tradingSymbol);

                await CheckForClosedTrades(bridge, tradingSymbol);
                await ManageOpenTrades(bridge, tradingSymbol);
            }
            catch (Exception ex)
            {
                using (LogContext.PushProperty("ops", true))
                using (LogContext.PushProperty("EventType", "Error"))
                {
                    logger.LogError(ex, "A critical error occurred in the position management cycle.");
                }
            }
        }
        logger.LogInformation("--- Position Management Cycle Finished ---");
    }

    private async Task ManageOpenTrades(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        if (OpenTradeTickets.Count == 0) return;
        logger.LogInformation("--- Managing {Count} open trades ---", OpenTradeTickets.Count);
        foreach (var ticket in OpenTradeTickets.ToList())
        {
            var response = await bridge.SendCommandAsync($"MANAGE_TRAILING_STOP,{ticket}");
            if (response.StartsWith("SUCCESS"))
            { 
                logger.LogInformation("Trailing stop for ticket {Ticket} was updated.", ticket);
            }
        }
    }

    private async Task CheckForClosedTrades(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        logger.LogInformation("--- Checking for closed trades on {Symbol} ---", tradingSymbol);
        try
        {
            var openTicketsStr = await bridge.SendCommandAsync($"GET_OPEN_TICKETS,{tradingSymbol}");
            var currentlyOpenTickets = new HashSet<long>();
            if (!string.IsNullOrEmpty(openTicketsStr) && !openTicketsStr.StartsWith("ERROR") && openTicketsStr != "NONE")
            {
                currentlyOpenTickets = openTicketsStr.Split(',').Select(long.Parse).ToHashSet();
            }
            
            var allTrackedTickets = OpenTradeTickets.ToList(); 
            foreach (var ticket in allTrackedTickets)
            {
                if (!currentlyOpenTickets.Contains(ticket))
                {
                    logger.LogInformation("Detected closed trade with ticket: {Ticket}. Fetching info...", ticket);
                    var tradeInfoStr = await bridge.SendCommandAsync($"GET_CLOSED_TRADE_INFO,{ticket}");
                    if (!tradeInfoStr.StartsWith("ERROR"))
                    {
                        var infoParts = tradeInfoStr.Split(',');
                        var profit = double.Parse(infoParts[1], CultureInfo.InvariantCulture);
                        using (LogContext.PushProperty("ops", true))
                        using (LogContext.PushProperty("EventType", "TradeClosed"))
                        {
                            logger.LogInformation("Trade closed. Ticket: {Ticket}, Profit: {Profit}", ticket, profit);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Could not get info for closed trade {Ticket}. Response: {Response}", ticket, tradeInfoStr);
                    }
                    OpenTradeTickets.Remove(ticket);
                }
            }
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("ops", true))
            using (LogContext.PushProperty("EventType", "Error"))
            {
                logger.LogError(ex, "An exception occurred in CheckForClosedTrades.");
            }
        }
    }
}