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
    
    private const string PhaseTimeframe = "H1";
    private const string PatternTimeframe = "M15";
    private const string EntryTimeframe = "M5";
    private const int HistoryCandleCount = 500;

    public async Task RunCycle()
    {
        logger.LogInformation("--- Main Cycle Started ---");
        using (var bridge = new MetaTraderPipeClient(logger))
        {
            try
            {
                bridge.Connect();
                
                string tradingSymbol = bridge.SendCommand("GET_CHART_SYMBOL");
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
                logger.LogError(ex, "A critical error occurred in the main trading cycle.");
            }
        }
        logger.LogInformation("--- Main Cycle Finished ---");
    }

    private async Task Execute(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        logger.LogInformation("--- Searching for new trade on {Symbol} ---", tradingSymbol);
        try
        {
            var openTradesStr = bridge.SendCommand($"COUNT_OPEN_TRADES,{tradingSymbol}");
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

            if (accountInfo.FreeMargin < 1000) return;
            if (!gate.CanExecuteTrade()) return;

            var symbolDetails = await GetSymbolDetails(bridge, tradingSymbol);
            if (symbolDetails == null) return;
            
            var historicalCandles = await GetHistoryData(bridge, tradingSymbol, PatternTimeframe, HistoryCandleCount);
            var zones = analyzer.DetectZones(historicalCandles, symbolDetails.PipSize * 15);
            IStrategyEngine chosenEngine = supervisor.SelectStrategy(marketDataPattern, zones);
            
            LastTradeSignalTimes.TryGetValue(tradingSymbol, out var lastTradeTime);
            var decision = chosenEngine.Evaluate(accountInfo, symbolDetails, marketDataPhase, marketDataPattern, marketDataEntry, lastTradeTime, zones, historicalCandles);
            
            if (decision.Action is ActionKind.Buy or ActionKind.Sell)
            {
                var command = $"{decision.Action.ToString().ToUpper()},{symbolDetails.SymbolName},{decision.PositionSizeLots},{decision.StopLossPrice},{decision.TakeProfitPrice}";
                var response = bridge.SendCommand(command);
                if (response.StartsWith("SUCCESS"))
                {
                    LastTradeSignalTimes[tradingSymbol] = marketDataEntry.OpenTime;
                    var ticket = long.Parse(response.Split(',')[1]);
                    if (!OpenTradeTickets.Contains(ticket)) OpenTradeTickets.Add(ticket);
                    gate.RegisterTrade();
                    
                    using (LogContext.PushProperty("ops", true))
                    using (LogContext.PushProperty("EventType", "TradeOpened"))
                    {
                        logger.LogInformation(
                            "Trade opened. Side: {Side}, Lots: {Lots}, Symbol: {Symbol}, EntryPrice: {EntryPrice}, StopLoss: {StopLoss}, TakeProfit: {TakeProfit}, Ticket: {Ticket}",
                            decision.Action.ToString(),
                            decision.PositionSizeLots,
                            symbolDetails.SymbolName,
                            decision.EntryPrice,
                            decision.StopLossPrice,
                            decision.TakeProfitPrice,
                            ticket);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("ops", true))
            using (LogContext.PushProperty("EventType", "Error"))
            {
                logger.LogError(ex, "An exception occurred in Execute method.");
            }
        }
    }

    private async Task<MarketData?> GetMarketDataForTimeframe(MetaTraderPipeClient bridge, string symbol, string timeframe)
    {
        var command = $"GET_MARKET_DATA,{symbol},{timeframe}";
        var marketDataStr = bridge.SendCommand(command);
        var marketParts = marketDataStr.Split(',');
        if (marketParts.Length < 12 || marketParts[0].StartsWith("ERROR"))
        {
            logger.LogError("Could not get valid market data for {Timeframe} from MT4: {Response}", timeframe, marketDataStr);
            return null;
        }
        return new MarketData
        {
            OpenTime = long.Parse(marketParts[0]),
            Open = double.Parse(marketParts[1], CultureInfo.InvariantCulture),
            High = double.Parse(marketParts[2], CultureInfo.InvariantCulture),
            Low = double.Parse(marketParts[3], CultureInfo.InvariantCulture),
            Close = double.Parse(marketParts[4], CultureInfo.InvariantCulture),
            Ask = double.Parse(marketParts[5], CultureInfo.InvariantCulture),
            Bid = double.Parse(marketParts[6], CultureInfo.InvariantCulture),
            EmaFast = double.Parse(marketParts[7], CultureInfo.InvariantCulture),
            EmaSlow = double.Parse(marketParts[8], CultureInfo.InvariantCulture),
            Atr = double.Parse(marketParts[9], CultureInfo.InvariantCulture),
            AtrSma = double.Parse(marketParts[10], CultureInfo.InvariantCulture),
            Adx = double.Parse(marketParts[11], CultureInfo.InvariantCulture)
        };
    }
    
    private async Task<List<MarketData>> GetHistoryData(MetaTraderPipeClient bridge, string symbol, string timeframe, int count)
    {
        var command = $"GET_HISTORY_DATA,{symbol},{timeframe},{count}";
        var response = bridge.SendCommand(command);
        var candles = new List<MarketData>();
        if (response.StartsWith("ERROR") || string.IsNullOrEmpty(response))
        {
            logger.LogError("Could not get historical data: {Response}", response);
            return candles;
        }
        var candleStrings = response.Split(';');
        foreach (var candleStr in candleStrings)
        {
            var parts = candleStr.Split(',');
            if (parts.Length < 5) continue;
            candles.Add(new MarketData
            {
                Open = double.Parse(parts[0], CultureInfo.InvariantCulture),
                High = double.Parse(parts[1], CultureInfo.InvariantCulture),
                Low = double.Parse(parts[2], CultureInfo.InvariantCulture),
                Close = double.Parse(parts[3], CultureInfo.InvariantCulture),
                OpenTime = long.Parse(parts[4])
            });
        }
        return candles;
    }

    private async Task<AccountInfo?> GetAccountInfo(MetaTraderPipeClient bridge)
    {
        var accountInfoStr = bridge.SendCommand("GET_ACCOUNT_INFO");
        var accountParts = accountInfoStr.Split(',');
        if (accountParts.Length < 5 || accountParts[0].StartsWith("ERROR")) return null;
        return new AccountInfo
        {
            AccountId = long.Parse(accountParts[0]),
            BrokerName = accountParts[1],
            Balance = double.Parse(accountParts[2], CultureInfo.InvariantCulture),
            Equity = double.Parse(accountParts[3], CultureInfo.InvariantCulture),
            FreeMargin = double.Parse(accountParts[4], CultureInfo.InvariantCulture)
        };
    }

    private async Task<SymbolDetails?> GetSymbolDetails(MetaTraderPipeClient bridge, string symbol)
    {
        var symbolInfoStr = bridge.SendCommand($"GET_SYMBOL_INFO,{symbol}");
        if (symbolInfoStr.StartsWith("ERROR"))
        {
            logger.LogError("Could not get symbol info: {Response}", symbolInfoStr);
            return null;
        }
        var symbolParts = symbolInfoStr.Split(',');
        return new SymbolDetails
        {
            SymbolName = symbol,
            LotSize = double.Parse(symbolParts[0], CultureInfo.InvariantCulture),
            AccountCurrency = symbolParts[1],
            QuoteCurrency = symbolParts[2],
            PipSize = double.Parse(symbolParts[3], CultureInfo.InvariantCulture),
            StepVolume = double.Parse(symbolParts[4], CultureInfo.InvariantCulture),
            QuoteToAccountRate = double.Parse(symbolParts[5], CultureInfo.InvariantCulture)
        };
    }

    private async Task CheckForClosedTrades(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        logger.LogInformation("--- Checking for closed trades on {Symbol} ---", tradingSymbol);
        try
        {
            var openTicketsStr = bridge.SendCommand($"GET_OPEN_TICKETS,{tradingSymbol}");
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
                    var tradeInfoStr = bridge.SendCommand($"GET_CLOSED_TRADE_INFO,{ticket}");
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
                logger.LogError(ex, "An exception occurred in Execute method.");
            }
        }
    }

    private async Task ManageOpenTrades(MetaTraderPipeClient bridge, string tradingSymbol)
    {
        if (OpenTradeTickets.Count == 0) return;
        logger.LogInformation("--- Managing {Count} open trades ---", OpenTradeTickets.Count);
        foreach (var ticket in OpenTradeTickets.ToList())
        {
            var response = bridge.SendCommand($"MANAGE_TRAILING_STOP,{ticket}");
            if (response.StartsWith("SUCCESS"))
            { 
                logger.LogInformation("Trailing stop for ticket {Ticket} was updated.", ticket);
            }
        }
    }
}