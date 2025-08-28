using System.Reactive.Linq;
using Domain;
using Microsoft.Extensions.Configuration;
using OpenAPI.Net;
using OpenAPI.Net.Helpers;
using Domain.Enum;

namespace Infrastructure;

public sealed class CTraderOrderExecutionPort : IOrderExecutionPort, IDisposable
{
    private readonly OpenClient _client;
    private readonly long _accountId;
    private readonly string _clientId, _clientSecret, _accessToken;

    public CTraderOrderExecutionPort(IConfiguration cfg)
    {
        var sec     = cfg.GetSection("CTrader");
        var modeStr = sec["Mode"] ?? "Demo";
        var mode    = modeStr.Equals("Live", StringComparison.OrdinalIgnoreCase) ? Mode.Live : Mode.Demo;
        var host    = ApiInfo.GetHost(mode); // "live"/"demo"

        _clientId     = sec["ClientId"]     ?? throw new ArgumentNullException("CTrader:ClientId");
        _clientSecret = sec["ClientSecret"] ?? throw new ArgumentNullException("CTrader:ClientSecret");
        _accessToken  = sec["AccessToken"]  ?? throw new ArgumentNullException("CTrader:AccessToken");
        _accountId    = long.Parse(sec["AccountId"] ?? "0");

        _client = new OpenClient(host, ApiInfo.Port, TimeSpan.FromSeconds(10));
        _client.Connect().GetAwaiter().GetResult();

        _client.SendMessage(new ProtoOAApplicationAuthReq {
            ClientId = _clientId, ClientSecret = _clientSecret
        }, ProtoOAPayloadType.ProtoOaApplicationAuthReq).GetAwaiter().GetResult();

        _client.SendMessage(new ProtoOAAccountAuthReq {
            CtidTraderAccountId = _accountId, AccessToken = _accessToken
        }, ProtoOAPayloadType.ProtoOaAccountAuthReq).GetAwaiter().GetResult();
    }

    public async Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        // 1) گرفتن symbolId
        var symbolsResTcs = new TaskCompletionSource<ProtoOASymbolsListRes>();
        using var symSub = _client.OfType<ProtoOASymbolsListRes>()
            .Where(r => r.CtidTraderAccountId == _accountId)
            .Subscribe(r => symbolsResTcs.TrySetResult(r));

        await _client.SendMessage(new ProtoOASymbolsListReq {
            CtidTraderAccountId = _accountId
        }, ProtoOAPayloadType.ProtoOaSymbolsListReq);

        var symbolsRes = await symbolsResTcs.Task;
        var symbol = symbolsRes.Symbol.FirstOrDefault(s => s.SymbolName == req.Symbol.Value)
                     ?? throw new InvalidOperationException($"Symbol '{req.Symbol.Value}' not found.");
        var symbolId = (long)symbol.SymbolId;

        // 2) ارسال سفارش مارکت و انتظارِ Filled
        var filledTcs = new TaskCompletionSource<(long orderId, long positionId)>();
        using var exeSub = _client.OfType<ProtoOAExecutionEvent>()
            .Where(e => e.CtidTraderAccountId == _accountId)
            .Subscribe(e =>
            {
                if (e.ExecutionType == ProtoOAExecutionType.OrderFilled && e.Position != null && e.Order != null)
                    filledTcs.TrySetResult(((long)e.Order.OrderId, (long)e.Position.PositionId));
                if (e.ExecutionType == ProtoOAExecutionType.OrderRejected && !string.IsNullOrEmpty(e.ErrorCode))
                    filledTcs.TrySetException(new Exception(e.ErrorCode));
            });

        await _client.SendMessage(new ProtoOANewOrderReq {
            CtidTraderAccountId = _accountId,
            SymbolId = symbolId,
            OrderType = ProtoOAOrderType.Market,
            TradeSide = req.Side == ActionKind.Buy ? ProtoOATradeSide.Buy : ProtoOATradeSide.Sell,
            Volume = (long)Math.Round(req.Lots * 100_000) 
        }, ProtoOAPayloadType.ProtoOaNewOrderReq);

        var (orderId, positionId) = await filledTcs.Task;

        
        await _client.SendMessage(new ProtoOAAmendPositionSLTPReq {
            CtidTraderAccountId = _accountId,
            PositionId = positionId,
            StopLoss   = req.Sl,
            TakeProfit = req.Tp
        }, ProtoOAPayloadType.ProtoOaAmendPositionSltpReq);

        return new TradeExecutionResult(true, new OrderId(orderId), "Executed via cTrader");
    }

    public Task<TradeExecutionResult> CloseByTpOrSlAsync(OrderId id, CancellationToken ct) =>
        Task.FromResult(new TradeExecutionResult(true, id, "Server closes at SL/TP"));

    public void Dispose() => _client?.Dispose();
}
