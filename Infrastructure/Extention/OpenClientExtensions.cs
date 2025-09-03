// In Project: Trader.Infrastructure/Helpers/OpenClientExtensions.cs
using Google.Protobuf;
using OpenAPI.Net;
using System;
using System.Reactive; // <<-- using جدید برای Observer
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Helpers;

public static class OpenClientExtensions
{
    public static Task<TRes> SendAndReceiveAsync<TRes>(this OpenClient client, IMessage request, ProtoOAPayloadType requestPayloadType) 
        where TRes : IMessage<TRes>, new()
    {
        var tcs = new TaskCompletionSource<TRes>();
        var responsePayloadType = GetResponsePayloadType(requestPayloadType);

        // ۱. ساخت یک Observer برای گوش دادن به پیام‌ها
        var observer = Observer.Create<IMessage>(
            onNext: message =>
            {
                // فقط به پیام‌های ProtoMessage گوش می‌دهیم
                if (message is not ProtoMessage protoMsg) return;

                // اگر پیام خطا بود، Task را با خطا به پایان می‌رسانیم
                if (protoMsg.PayloadType == (uint)ProtoOAPayloadType.ProtoOaErrorRes)
                {
                    var errorRes = ProtoOAErrorRes.Parser.ParseFrom(protoMsg.Payload);
                    tcs.TrySetException(new InvalidOperationException($"cTrader API Error: {errorRes.Description} ({errorRes.ErrorCode})"));
                }
                // اگر پیام، پاسخ مورد انتظار ما بود، Task را با موفقیت به پایان می‌رسانیم
                else if (protoMsg.PayloadType == (uint)responsePayloadType)
                {
                    var response = new TRes();
                    response.MergeFrom(protoMsg.Payload);
                    tcs.TrySetResult(response);
                }
            },
            onError: ex => tcs.TrySetException(ex),
            onCompleted: () => tcs.TrySetCanceled()
        );

        // ۲. Subscribe کردن Observer به کلاینت
        // این متد یک IDisposable برمی‌گرداند که باید آن را مدیریت کنیم
        using var subscription = client.Subscribe(observer);

        async Task SendWithTimeout()
        {
            try
            {
                await client.SendMessage(request, requestPayloadType);
                if (await Task.WhenAny(tcs.Task, Task.Delay(15000)) != tcs.Task)
                {
                    tcs.TrySetException(new TimeoutException($"Timeout waiting for response of type {typeof(TRes).Name}"));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        _ = SendWithTimeout();
        return tcs.Task;
    }

    private static ProtoOAPayloadType GetResponsePayloadType(ProtoOAPayloadType requestType)
    {
        var name = requestType.ToString().Replace("Req", "Res");
        if (Enum.TryParse<ProtoOAPayloadType>(name, out var responseType)) return responseType;
        if (requestType == ProtoOAPayloadType.ProtoOaNewOrderReq) return ProtoOAPayloadType.ProtoOaExecutionEvent;
        throw new InvalidOperationException($"Could not find a matching response type for {requestType}");
    }
}