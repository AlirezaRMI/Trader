using Domain;
using Infrastructure.Helpers;
using Infrastructure.Maine;
using Microsoft.Extensions.Logging;
using OpenAPI.Net;

namespace Infrastructure.Extention;

public sealed class CTraderOrderExecutionPort 
{
    private readonly OpenClient _client;
    private readonly long _accountId;
    private readonly ILogger<CTraderOrderExecutionPort> _log;
    
    public CTraderOrderExecutionPort(CTraderSession session, ILogger<CTraderOrderExecutionPort> log)
    {
        _client = session.Client;
        _accountId = session.AccountId;
        _log = log;
    }

    public async Task<TradeExecutionResult> OpenMarketAsync(TradeRequest req, CancellationToken ct)
    {
        try
        {
            // ... (منطق گرفتن symbol id) ...
            var orderReq = new ProtoOANewOrderReq { /* ... mapping ... */ };
            var response = await _client.SendAndReceiveAsync<ProtoOAExecutionEvent>(orderReq, ProtoOAPayloadType.ProtoOaNewOrderReq);

            // حالا ExecutionType وجود دارد چون response از نوع صحیح است
            if (response.ExecutionType == ProtoOAExecutionType.OrderAccepted || response.ExecutionType == ProtoOAExecutionType.OrderFilled)
            {
                return TradeExecutionResult.Success(new OrderId((long)response.Order.OrderId));
            }
            else
            {
                return TradeExecutionResult.Failure(response.ErrorCode ?? "Order Rejected");
            }
        }
        catch (Exception ex) { return TradeExecutionResult.Failure(ex.Message); }
    }
}