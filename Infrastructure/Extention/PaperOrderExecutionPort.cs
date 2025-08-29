using Domain;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Extention;

public sealed class PaperOrderExecutionPort(ILogger<PaperOrderExecutionPort> logger) : IOrderExecutionPort
{
    private long _nextId = 100000;

    public Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        var id = new OrderId(Interlocked.Increment(ref _nextId));
        logger.LogInformation("Paper EXEC {Side} {Lots} {Symbol} SL={SL} TP={TP} -> #{Id}",
            req.Side, req.Lots, req.Symbol.Value, req.Sl, req.Tp, id);
        return Task.FromResult(new TradeExecutionResult(true, id, "Executed (paper)"));
     
    }
    public Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct)
        => Task.FromResult(new TradeExecutionResult(true, id, "Closed (paper)"));
}