
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAPI.Net;
using OpenAPI.Net.Helpers;

namespace Infrastructure.Maine;

public sealed class CTraderOpenApiSession : IDisposable
{
    private readonly CTraderOpenApiOptions _opt;
    private readonly IDisposable _errSub;

    public OpenClient Client { get; }
    public long AccountId => _opt.AccountId;
    
    private static string ResolveHost(string? mode) =>
        string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase)
            ? "live.ctraderapi.com"
            : "demo.ctraderapi.com";

    public CTraderOpenApiSession(IOptions<CTraderOpenApiOptions> opt, ILogger<CTraderOpenApiSession> log)
    {
        ILogger<CTraderOpenApiSession> log1 = log;
        _opt = opt.Value;

        var host = ResolveHost(_opt.Mode);             
        Client   = new OpenClient(host, ApiInfo.Port, TimeSpan.FromSeconds(10));
        Client.Connect().GetAwaiter().GetResult();
        _errSub = Client.Subscribe(_ => { }, ex => log1.LogError(ex, "OpenAPI stream error"));
        Client.SendMessage(new ProtoOAApplicationAuthReq
        {
            ClientId     = _opt.ClientId,
            ClientSecret = _opt.ClientSecret
        }, ProtoOAPayloadType.ProtoOaApplicationAuthReq).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(_opt.AccessToken) && _opt.AccountId > 0)
        {
            Client.SendMessage(new ProtoOAAccountAuthReq
            {
                CtidTraderAccountId = _opt.AccountId,
                AccessToken         = _opt.AccessToken
            }, ProtoOAPayloadType.ProtoOaAccountAuthReq).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        try { _errSub?.Dispose(); } catch { /* ignore */ }
        try { Client?.Dispose(); } catch { /* ignore */ }
    }
}
