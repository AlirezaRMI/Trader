using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Extention;

public class TradingJob(StrategyEngine engine, DailyTradePolicy gate, ILogger<TradingJob> logger)
{
    public async Task Execute()
    {
        logger.LogInformation("--- Trading Job Started ---");

        if (!gate.CanExecuteTrade())
        {
            logger.LogInformation("Daily trade limit reached. No trades will be executed.");
            return;
        }

        var marketDataStr = MetaTraderPipeClient.SendCommand("GET_MARKET_DATA,EURUSD", logger);
        var marketParts = marketDataStr.Split(',');
        if (marketParts[0] == "ERROR") { logger.LogError("Could not get market data from MT4: {Error}", marketDataStr); return; }

        var marketData = new MarketData
        {
            Open = double.Parse(marketParts[0]), High = double.Parse(marketParts[1]),
            Low = double.Parse(marketParts[2]), Close = double.Parse(marketParts[3]),
            Ask = double.Parse(marketParts[4]), Bid = double.Parse(marketParts[5]),
            Atr = double.Parse(marketParts[6])
        };

        // ۳. گرفتن اطلاعات حساب از متاتریدر
        var accountInfoStr = MetaTraderPipeClient.SendCommand("GET_ACCOUNT_INFO", logger);
        var accountParts = accountInfoStr.Split(',');
        if (accountParts[0] == "ERROR") { logger.LogError("Could not get account info from MT4: {Error}", accountInfoStr); return; }

        var accountInfo = new AccountInfo
        {
            AccountId = long.Parse(accountParts[0]), BrokerName = accountParts[1],
            Balance = double.Parse(accountParts[2]), Equity = double.Parse(accountParts[3])
        };

        // ۴. اجرای استراتژی و گرفتن تصمیم
        // (اطلاعات نماد را فعلاً به صورت ثابت فرض می‌کنیم)
        var symbolInfo = new SymbolInfo { PipSize = 0.0001, StepVolume = 0.01 };
        var decision = engine.Evaluate(accountInfo, symbolInfo, marketData);

        logger.LogInformation("Strategy decision: {Action}. Note: {Note}", decision.Action, decision.Note);

        // ۵. اجرای معامله در صورت لزوم
        if (decision.Action == Domain.Enum.ActionKind.Buy || decision.Action == Domain.Enum.ActionKind.Sell)
        {
            var command = $"{decision.Action.ToString().ToUpper()},EURUSD,{decision.PositionSizeLots},{decision.StopLossPrice},{decision.TakeProfitPrice}";
            
            logger.LogInformation("Sending trade command to MT4: {Command}", command);
            var response = MetaTraderPipeClient.SendCommand(command, logger);
            
            logger.LogInformation("Response from MT4: {Response}", response);
            
            // اگر معامله موفق بود، آن را در سیاست روزانه ثبت می‌کنیم
            if (response.StartsWith("SUCCESS"))
            {
                gate.RegisterTrade();
            }
        }
        
        logger.LogInformation("--- Trading Job Finished ---");
        await Task.CompletedTask;
    }
}