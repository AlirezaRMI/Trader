using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAPI.Net;

namespace Infrastructure.Maine;

public sealed class CTraderSession : IDisposable
{
    private readonly CTraderOpenApiOptions _options;
    private readonly ILogger<CTraderSession> _log;
    
    public OpenClient Client { get; }
    public long AccountId => _options.AccountId;

    public CTraderSession(IOptions<CTraderOpenApiOptions> options, ILogger<CTraderSession> log)
    {
        _options = options.Value;
        _log = log;

        var host = _options.Mode.Equals("Live", StringComparison.OrdinalIgnoreCase)
            ? "live.ctraderapi.com"
            : "demo.ctraderapi.com";
        
        Client = new OpenClient(host, 5035, TimeSpan.FromSeconds(10));
        
        
        ConnectAndAuthorizeAsync().GetAwaiter().GetResult();
    }

    private async Task ConnectAndAuthorizeAsync()
    {
        await Client.Connect();
        _log.LogInformation("Successfully connected to cTrader.");

        var appAuthReq = new ProtoOAApplicationAuthReq { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret };
        await Client.SendMessage(appAuthReq, ProtoOAPayloadType.ProtoOaApplicationAuthReq);

        var accAuthReq = new ProtoOAAccountAuthReq { CtidTraderAccountId = AccountId, AccessToken = _options.AccessToken };
        await Client.SendMessage(accAuthReq, ProtoOAPayloadType.ProtoOaAccountAuthReq);
        
        _log.LogInformation("cTrader session is fully connected and authorized.");
    }

    public void Dispose() => Client.Dispose();
}