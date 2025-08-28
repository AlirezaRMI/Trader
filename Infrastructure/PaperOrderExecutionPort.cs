using Domain;

namespace Infrastructure;

public sealed class PaperOrderExecutionPort : IOrderExecutionPort
{
    private long _nextId = 100000;

    public Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        var id = new OrderId(Interlocked.Increment(ref _nextId));
        return Task.FromResult(new TradeExecutionResult(true, id, "Executed (paper)"));
    }

    public Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct)
        => Task.FromResult(new TradeExecutionResult(true, id, "Closed (paper)"));
}