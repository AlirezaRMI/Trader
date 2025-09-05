using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Hangfire;
using Infrastructure.Helpers;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Infrastructure.Extention;

[DisableConcurrentExecution(timeoutInSeconds: 60)]
public class TradingJob(StrategyEngine engine, DailyTradePolicy gate, ILogger<TradingJob> logger)
{
    private static long _lastCandleTimestamp;
    private static readonly List<long> OpenTradeTickets = new();

    public async Task RunCycle()
    {
        logger.LogInformation("--- Main Cycle Started ---");
        using (var bridge = new MetaTraderPipeClient(logger))
        {
            try
            {
                bridge.Connect();
                await CheckForClosedTrades(bridge);
                await Execute(bridge);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A critical error occurred in the main trading cycle.");
            }
        }

        logger.LogInformation("--- Main Cycle Finished ---");
    }

    private async Task Execute(MetaTraderPipeClient bridge)
    {
        logger.LogInformation("--- Searching for new trade ---");
        try
        {
            var openTradesStr = bridge.SendCommand("COUNT_OPEN_TRADES,EURUSD");
            if (int.Parse(openTradesStr) > 0)
            {
                logger.LogInformation("An open trade already exists for EURUSD. Skipping.");
                return;
            }

            var marketDataStr = bridge.SendCommand("GET_MARKET_DATA,EURUSD");
            var marketParts = marketDataStr.Split(',');

            if (marketParts.Length < 108 || marketParts[0].StartsWith("ERROR"))
            {
                logger.LogError("Could not get valid market data from MT4: {Response}", marketDataStr);
                return;
            }

            long currentCandleTimestamp = long.Parse(marketParts[0]);
            if (currentCandleTimestamp <= _lastCandleTimestamp)
            {
                logger.LogInformation("Not a new candle. Skipping.");
                return;
            }

            var marketData = new MarketData
            {
                OpenTime = currentCandleTimestamp,
                Open = double.Parse(marketParts[1]), High = double.Parse(marketParts[2]),
                Low = double.Parse(marketParts[3]), Close = double.Parse(marketParts[4]),
                Ask = double.Parse(marketParts[5]), Bid = double.Parse(marketParts[6]),
                Atr = double.Parse(marketParts[7])
            };

            var recentCloses = marketParts.Skip(8).Select(double.Parse).ToList();

            var accountInfoStr = bridge.SendCommand("GET_ACCOUNT_INFO");
            var accountParts = accountInfoStr.Split(',');
            if (accountParts.Length < 5 || accountParts[0].StartsWith("ERROR")) return;

            var accountInfo = new AccountInfo
            {
                AccountId = long.Parse(accountParts[0]), BrokerName = accountParts[1],
                Balance = double.Parse(accountParts[2]), Equity = double.Parse(accountParts[3]),
                FreeMargin = double.Parse(accountParts[4])
            };

            if (accountInfo.FreeMargin < 1000) return;
            if (!gate.CanExecuteTrade()) return;

            var symbolInfo = new SymbolInfo
                {SymbolName = "EURUSD", PipSize = 0.0001, StepVolume = 0.01, Digits = 5, LotSize = 100000};

            var decision = engine.Evaluate(accountInfo, symbolInfo, marketData, recentCloses.Take(21).ToList());
            if (decision.Action == ActionKind.Buy || decision.Action == ActionKind.Sell)
            {
                var command =
                    $"{decision.Action.ToString().ToUpper()},{symbolInfo.SymbolName},{decision.PositionSizeLots},{decision.StopLossPrice},{decision.TakeProfitPrice}";

                var response = bridge.SendCommand(command);

                if (response.StartsWith("SUCCESS"))
                {
                    var ticket = long.Parse(response.Split(',')[1]);
                    if (!OpenTradeTickets.Contains(ticket))
                    {
                        OpenTradeTickets.Add(ticket);
                    }

                    gate.RegisterTrade();
                    _lastCandleTimestamp = currentCandleTimestamp;

                    using (LogContext.PushProperty("ops", true))
                    using (LogContext.PushProperty("EventType", "TradeOpened"))
                    {
                        logger.LogInformation(
                            "Trade opened. Side: {Side}, Lots: {Lots}, Symbol: {Symbol}, EntryPrice: {EntryPrice}, StopLoss: {StopLoss}, TakeProfit: {TakeProfit}, Ticket: {Ticket}",
                            decision.Action.ToString(),
                            decision.PositionSizeLots,
                            symbolInfo.SymbolName,
                            marketData.Close,
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

        await Task.CompletedTask;
    }

    private async Task CheckForClosedTrades(MetaTraderPipeClient bridge)
    {
        logger.LogInformation("--- Checking for closed trades ---");
        try
        {
            var openTicketsStr = bridge.SendCommand("GET_OPEN_TICKETS,EURUSD");

            var currentlyOpenTickets = new HashSet<long>();
            if (!string.IsNullOrEmpty(openTicketsStr) && !openTicketsStr.StartsWith("ERROR"))
            {
                currentlyOpenTickets = openTicketsStr.Split(',')
                    .Select(long.Parse)
                    .ToHashSet();
            }

            var closedTickets = OpenTradeTickets.Except(currentlyOpenTickets).ToList();

            foreach (var ticket in closedTickets)
            {
                logger.LogInformation("Detected closed trade with ticket: {Ticket}. Fetching info...", ticket);
                var tradeInfoStr = bridge.SendCommand($"GET_CLOSED_TRADE_INFO,{ticket}");
                if (!tradeInfoStr.StartsWith("ERROR"))
                {
                    var infoParts = tradeInfoStr.Split(',');
                    var profit = double.Parse(infoParts[1]);

                    using (LogContext.PushProperty("ops", true))
                    using (LogContext.PushProperty("EventType", "TradeClosed"))
                    {
                        logger.LogInformation("Trade closed. Ticket: {Ticket}, Profit: {Profit}", ticket, profit);
                    }
                }
                else
                {
                    logger.LogWarning("Could not get info for closed trade {Ticket}. Response: {Response}", ticket,
                        tradeInfoStr);
                }
            }

            OpenTradeTickets.Clear();
            OpenTradeTickets.AddRange(currentlyOpenTickets);
            logger.LogInformation("Synced open trades list. Currently open: {Count}", OpenTradeTickets.Count);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("ops", true))
            using (LogContext.PushProperty("EventType", "Error"))
            {
                logger.LogError(ex, "An error occurred while checking for closed trades.");
            }
        }

        await Task.CompletedTask;
    }
}