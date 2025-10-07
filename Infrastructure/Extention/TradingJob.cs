using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Domain.Services.Interfaces;
using Hangfire;
using Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using System.Globalization;


namespace Infrastructure.Extention;

[DisableConcurrentExecution(timeoutInSeconds: 60)]
public class TradingJob(
    MarketSupervisor supervisor,
    PriceActionAnalyzer analyzer,
    DailyTradePolicy gate,
    ILogger<TradingJob> logger,
    IEconomicCalendarService service)
{
    private static readonly List<long> OpenTradeTickets = new();
    private static readonly Dictionary<string, long> LastTradeSignalTimes = new(); 
    private static readonly Dictionary<string, (List<MarketData> History, DateTime FetchedAt)> HistoryCache = new();  
    
    private const string PhaseTimeframe = "H1";
    private const string PatternTimeframe = "M15";
    private const string EntryTimeframe = "M5";
    private const int HistoryCandleCount = 500;
    private const double ZoneMergeThreshold = 0.001;
    private static readonly TimeSpan HistoryCacheExpiry = TimeSpan.FromMinutes(5);

    public async Task RunCycle()
    {
        logger.LogInformation("--- Main Cycle Started ---");
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
                
                logger.LogInformation(">>>>>> Operating on symbol: {Symbol} <<<<<<", tradingSymbol);

                await service.RefreshCalendarAsync();
                if (service.IsInEmbargoPeriod(tradingSymbol, 30, 30))
                {
                    logger.LogWarning("Execution paused due to upcoming high-impact news for {Symbol}.", tradingSymbol);
                    return; 
                }
                
                await CheckForClosedTrades(bridge, tradingSymbol);
                await ManageOpenTrades(bridge, tradingSymbol);
                await Execute(bridge, tradingSymbol);
            }
            catch (Exception ex)
            {
                using (LogContext.PushProperty("ops", true))
                using (LogContext.PushProperty("EventType", "Error"))
                {
                    logger.LogError(ex, "A critical error occurred in the main trading cycle.");
                }
            }
        }
        logger.LogInformation("--- Main Cycle Finished ---");
    }

    private async Task Execute(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        logger.LogInformation("--- Searching for new trade on {Symbol} ---", tradingSymbol);
        try
        {
            var openTradesStr = await bridge.SendCommandAsync($"COUNT_OPEN_TRADES,{tradingSymbol}");
            if (int.Parse(openTradesStr) > 0)
            {
                logger.LogInformation("An open trade already exists for {Symbol}. Skipping.", tradingSymbol);
                return;
            }

            var marketDataPhase = await GetMarketDataForTimeframe(bridge, tradingSymbol, PhaseTimeframe);
            if (marketDataPhase == null) return;
            var marketDataPattern = await GetMarketDataForTimeframe(bridge, tradingSymbol, PatternTimeframe);
            if (marketDataPattern == null) return;
            var marketDataEntry = await GetMarketDataForTimeframe(bridge, tradingSymbol, EntryTimeframe);
            if (marketDataEntry == null) return;
            
            var accountInfo = await GetAccountInfo(bridge);
            if (accountInfo == null) return;
            
            var symbolDetails = await GetSymbolDetails(bridge, tradingSymbol);
            if (symbolDetails == null) return;
            
            var history = await GetHistoryDataCached(bridge, tradingSymbol, EntryTimeframe, HistoryCandleCount);
            if (history.Count < 2) 
            {
                logger.LogWarning("Not enough history data for {Symbol}. Skipping.", tradingSymbol);
                return;
            }
            
            var lastTradeSignalTime = LastTradeSignalTimes.TryGetValue(tradingSymbol, out var time) ? time : 0L;
            
            var zones = await GetZones(bridge, tradingSymbol, history);
            
            var engine = supervisor.SelectStrategy(marketDataPattern, zones);
            
            var decision = engine.Evaluate(accountInfo, symbolDetails, marketDataPhase, marketDataPattern, marketDataEntry, lastTradeSignalTime, zones, history);
            
            if (decision.Action == ActionKind.Hold)
            {
                logger.LogInformation("No trade signal for {Symbol}.", tradingSymbol);
                return;
            }
            
            decision = decision with { EntryPrice = decision.Action == ActionKind.Buy ? marketDataEntry.Ask : marketDataEntry.Bid };
            
            if (!gate.IsAllowed(decision.Action, accountInfo.Equity))
            {
                logger.LogWarning("Daily policy gate blocked trade for {Symbol}.", tradingSymbol);
                return;
            }
            
            gate.RegisterTrade(decision.Action);
            
            var maxRetries = 2;
            string tradeResponse = null;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                var command = decision.Action == ActionKind.Buy 
                    ? $"BUY,{tradingSymbol},{decision.PositionSizeLots:F2},{decision.StopLossPrice:F5},{decision.TakeProfitPrice:F5}" 
                    : $"SELL,{tradingSymbol},{decision.PositionSizeLots:F2},{decision.StopLossPrice:F5},{decision.TakeProfitPrice:F5}";
                
                tradeResponse = await bridge.SendCommandAsync(command);
                logger.LogDebug("Trade command sent: {Command}, Response: {Response}", command, tradeResponse);
                
                if (IsValidTradeResponse(tradeResponse))
                {
                    if (!tradeResponse.StartsWith("ERROR")) break;
                }
                else
                {
                    logger.LogWarning("Invalid trade response (looks like history data): {Response}", tradeResponse);
                    tradeResponse = "ERROR,InvalidResponse";
                }
                
                logger.LogWarning("Trade retry {Retry}/{MaxRetries} failed for {Symbol}: {Response}", retry + 1, maxRetries, tradingSymbol, tradeResponse);
                await Task.Delay(2000 * (retry + 1));
            }
            
            if (tradeResponse.StartsWith("SUCCESS"))
            {
                var parts = tradeResponse.Split(',');
                if (parts.Length > 1 && long.TryParse(parts[1], out var ticketId))
                {
                    OpenTradeTickets.Add(ticketId);
                    LastTradeSignalTimes[tradingSymbol] = marketDataEntry.OpenTime;
                    
                    using (LogContext.PushProperty("ops", true))
                    using (LogContext.PushProperty("EventType", "TradeOpened"))
                    using (LogContext.PushProperty("Side", decision.Action.ToString()))
                    using (LogContext.PushProperty("Lots", decision.PositionSizeLots))
                    using (LogContext.PushProperty("Symbol", tradingSymbol))
                    using (LogContext.PushProperty("Ticket", ticketId))
                    using (LogContext.PushProperty("EntryPrice", decision.EntryPrice))
                    using (LogContext.PushProperty("StopLoss", decision.StopLossPrice))
                    using (LogContext.PushProperty("TakeProfit", decision.TakeProfitPrice))
                    {
                        logger.LogInformation("Trade opened successfully for {Symbol}. Ticket: {Ticket}", tradingSymbol, ticketId);
                    }
                }
                else
                {
                    logger.LogError("SUCCESS response but invalid ticket: {Response}", tradeResponse);
                }
            }
            else
            {
                logger.LogError("All trade retries failed for {Symbol}: {Response}", tradingSymbol, tradeResponse);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Execute for {Symbol}", tradingSymbol);
        }
    }

    private bool IsValidTradeResponse(string response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        var trimmed = response.Trim();
        return trimmed.StartsWith("SUCCESS,") || trimmed.StartsWith("ERROR,") || trimmed.StartsWith("NO_CHANGE,");
    }

    private async Task<List<MarketData>> GetHistoryDataCached(MetaTraderPipeClient bridge, string symbol, string tf, int count)
    {
        var cacheKey = $"{symbol}_{tf}";
        if (HistoryCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.FetchedAt < HistoryCacheExpiry)
        {
            logger.LogDebug("Using cached history for {Symbol}/{TF}", symbol, tf);
            return cached.History;
        }

        var history = await GetHistoryData(bridge, symbol, tf, count);
        HistoryCache[cacheKey] = (history, DateTime.UtcNow);
        return history;
    }

    private async Task<MarketData?> GetMarketDataForTimeframe(MetaTraderPipeClient bridge, string symbol, string tf)
    {
        var response = await bridge.SendCommandAsync($"GET_MARKET_DATA,{symbol},{tf}");
        if (response.StartsWith("ERROR")) 
        {
            logger.LogError("Failed to get market data for {Symbol}/{TF}: {Response}", symbol, tf, response);
            return null;
        }
        var parts = response.Split(',');
        if (parts.Length < 12) return null;
        return new MarketData
        {
            OpenTime = long.Parse(parts[0], CultureInfo.InvariantCulture),
            Open = double.Parse(parts[1], CultureInfo.InvariantCulture),
            High = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Low = double.Parse(parts[3], CultureInfo.InvariantCulture),
            Close = double.Parse(parts[4], CultureInfo.InvariantCulture),
            Ask = double.Parse(parts[5], CultureInfo.InvariantCulture),
            Bid = double.Parse(parts[6], CultureInfo.InvariantCulture),
            EmaFast = double.Parse(parts[7], CultureInfo.InvariantCulture),
            EmaSlow = double.Parse(parts[8], CultureInfo.InvariantCulture),
            Atr = double.Parse(parts[9], CultureInfo.InvariantCulture),
            AtrSma = double.Parse(parts[10], CultureInfo.InvariantCulture),
            Adx = double.Parse(parts[11], CultureInfo.InvariantCulture)
        };
    }

    private async Task<AccountInfo?> GetAccountInfo(MetaTraderPipeClient bridge)
    {
        var response = await bridge.SendCommandAsync("GET_ACCOUNT_INFO");
        if (response.StartsWith("ERROR")) return null;
        var parts = response.Split(',');
        if (parts.Length < 5) return null;
        return new AccountInfo
        {
            AccountId = long.Parse(parts[0], CultureInfo.InvariantCulture),
            BrokerName = parts[1],
            Balance = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Equity = double.Parse(parts[3], CultureInfo.InvariantCulture),
            FreeMargin = double.Parse(parts[4], CultureInfo.InvariantCulture)
        };
    }

    private async Task<SymbolDetails?> GetSymbolDetails(MetaTraderPipeClient bridge, string symbol)
    {
        var response = await bridge.SendCommandAsync($"GET_SYMBOL_INFO,{symbol}");
        if (response.StartsWith("ERROR")) return null;
        var parts = response.Split(',');
        if (parts.Length < 6) return null;
        var details = new SymbolDetails();
        details.SymbolName = symbol;
        details.LotSize = double.Parse(parts[0], CultureInfo.InvariantCulture);
        details.AccountCurrency = parts[1];
        details.QuoteCurrency = parts[2];
        details.PipSize = double.Parse(parts[3], CultureInfo.InvariantCulture);
        details.StepVolume = double.Parse(parts[4], CultureInfo.InvariantCulture);
        details.QuoteToAccountRate = double.Parse(parts[5], CultureInfo.InvariantCulture);
        return details;
    }

    private async Task<List<MarketData>> GetHistoryData(MetaTraderPipeClient bridge, string symbol, string tf, int count)
    {
        var response = await bridge.SendCommandAsync($"GET_HISTORY_DATA,{symbol},{tf},{count}");
        logger.LogDebug("History response for {Symbol}: {Response}", symbol, response);
        var history = new List<MarketData>();
        if (response.StartsWith("ERROR")) return history;
        var candles = response.Split(';');
        foreach (var candleStr in candles)
        {
            if (string.IsNullOrWhiteSpace(candleStr)) continue;
            var parts = candleStr.Split(',');
            if (parts.Length >= 5)
            {
                history.Add(new MarketData
                {
                    OpenTime = long.Parse(parts[4], CultureInfo.InvariantCulture),
                    Open = double.Parse(parts[0], CultureInfo.InvariantCulture),
                    High = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    Low = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    Close = double.Parse(parts[3], CultureInfo.InvariantCulture),
                    Ask = 0,
                    Bid = 0,
                    EmaFast = 0,
                    EmaSlow = 0,
                    Atr = 0,
                    AtrSma = 0,
                    Adx = 0
                });
            }
        }
        return history;
    }

    private async Task<List<PriceZone>> GetZones(MetaTraderPipeClient bridge, string symbol, List<MarketData> history)
    {
        var zones = analyzer.DetectZones(history, ZoneMergeThreshold);
        logger.LogInformation("Detected {ZoneCount} zones for {Symbol} with threshold {Threshold}.", zones.Count, symbol, ZoneMergeThreshold);
        return zones;
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
}