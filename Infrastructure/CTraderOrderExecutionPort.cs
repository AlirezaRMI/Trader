
using System.Reactive.Linq;
using Domain;
using Domain.Enum;
using Infrastructure.Maine;
using Microsoft.Extensions.Logging;
using OpenAPI.Net;


namespace Infrastructure;

public sealed class CTraderOrderExecutionPort(CTraderOpenApiSession session, ILogger<CTraderOrderExecutionPort> log)
    : IOrderExecutionPort
{
    private OpenClient Client => session.Client;  
    private long AccountId     => session.AccountId;

    public async Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        
        var symbolsTcs = new TaskCompletionSource<ProtoOASymbolsListRes>();
        using var symSub = Client.OfType<ProtoOASymbolsListRes>()
            .Where(r => r.CtidTraderAccountId == AccountId)
            .Subscribe(r => symbolsTcs.TrySetResult(r));

        await Client.SendMessage(new ProtoOASymbolsListReq
        {
            CtidTraderAccountId = AccountId
        }, ProtoOAPayloadType.ProtoOaSymbolsListReq);

        var symbolsRes = await symbolsTcs.Task;
        var light = symbolsRes.Symbol.First(s => s.SymbolName == req.Symbol.Value);
        var symbolId = (long)light.SymbolId;
        
        var byIdTcs = new TaskCompletionSource<ProtoOASymbolByIdRes>();
        using var byIdSub = Client.OfType<ProtoOASymbolByIdRes>()
            .Where(r => r.CtidTraderAccountId == AccountId)
            .Subscribe(r => byIdTcs.TrySetResult(r));

        await Client.SendMessage(new ProtoOASymbolByIdReq
        {
            CtidTraderAccountId = AccountId,
            SymbolId = { symbolId }
        }, ProtoOAPayloadType.ProtoOaSymbolByIdReq);

        var byId   = await byIdTcs.Task;
        var symbol = byId.Symbol.First(s => (long)s.SymbolId == symbolId);

        
        long step = symbol.StepVolume;   
        long min  = symbol.MinVolume;    
        long lot  = symbol.LotSize;      

        long proposed = (long)Math.Round(req.Lots * lot);
        long volume   = Math.Max(min, (proposed / step) * step); 
        
        var filledTcs = new TaskCompletionSource<(long orderId, long positionId)>();
        using var exeSub = Client.OfType<ProtoOAExecutionEvent>()
            .Where(e => e.CtidTraderAccountId == AccountId)
            .Subscribe(e =>
            {
                if (e.ExecutionType == ProtoOAExecutionType.OrderFilled &&
                    e.Position is not null && e.Order is not null)
                {
                    filledTcs.TrySetResult(((long)e.Order.OrderId, (long)e.Position.PositionId));
                }
                if (e.ExecutionType == ProtoOAExecutionType.OrderRejected &&
                    !string.IsNullOrEmpty(e.ErrorCode))
                {
                    filledTcs.TrySetException(new Exception(e.ErrorCode));
                }
            });

        await Client.SendMessage(new ProtoOANewOrderReq
        {
            CtidTraderAccountId = AccountId,
            SymbolId      = symbolId,
            OrderType     = ProtoOAOrderType.Market,
            TradeSide     = req.Side == ActionKind.Buy ? ProtoOATradeSide.Buy : ProtoOATradeSide.Sell,
            Volume        = volume,
            Comment       = req.Comment,
            ClientOrderId = $"cli-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
        }, ProtoOAPayloadType.ProtoOaNewOrderReq);

        var (orderId, positionId) = await filledTcs.Task;

        int digits = (int)symbol.Digits;
        double? sl = req.Sl > 0 ? Math.Round(req.Sl, digits) : null;
        double? tp = req.Tp > 0 ? Math.Round(req.Tp, digits) : null;

        if (sl.HasValue || tp.HasValue)
        {
            await Client.SendMessage(new ProtoOAAmendPositionSLTPReq
            {
                CtidTraderAccountId = AccountId,
                PositionId = positionId,
                StopLoss   = sl ?? 0,
                TakeProfit = tp ?? 0
            }, ProtoOAPayloadType.ProtoOaAmendPositionSltpReq);
        }

        log.LogInformation("OpenAPI: Filled {Side} {Lots} {Symbol} @Order#{OrderId} Pos#{PosId}",
            req.Side, req.Lots, req.Symbol.Value, orderId, positionId);

        return new TradeExecutionResult(true, new OrderId(orderId), "Executed via cTrader");
    }

    public Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct)
        => Task.FromResult(new TradeExecutionResult(true, id, "Server will close at SL/TP"));
}
