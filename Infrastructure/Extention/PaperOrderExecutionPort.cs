// In Project: Trader.Infrastructure/Extention/PaperOrderExecutionPort.cs
using Domain;
using Domain.Services;
using Domain.Trading;
using Microsoft.Extensions.Logging;
using System.Threading;


namespace Infrastructure.Extention;

public sealed class PaperOrderExecutionPort(ILogger<PaperOrderExecutionPort> logger)
{
    private long _nextId = 100000;

    public Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        var id = new OrderId(Interlocked.Increment(ref _nextId));
        logger.LogInformation("Paper EXEC {Action} {Lots} {Symbol} SL={SL} TP={TP} -> #{Id}",
            req.Action, req.PositionSizeLots, req.Symbol.Value, req.StopLossPrice, req.TakeProfitPrice, id);
        return Task.FromResult(TradeExecutionResult.Success(id, "Executed (paper)"));
    }
    
    public Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct) 
        => Task.FromResult(TradeExecutionResult.Success(id, "Closed (paper)"));
}