using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenAPI.Net.Auth;
using Serilog.Context;
using System.Reactive.Linq;
using Infrastructure.Maine;

namespace Api.Controllers;

[ApiController]
[Route("api/ct")]
public sealed class CTraderController(
    IOptions<CTraderOpenApiOptions> opt,
    CTraderOpenApiSession session,
    ILogger<CTraderController> log)
    : ControllerBase
{
    private readonly CTraderOpenApiOptions _opt = opt.Value;


    [HttpGet("auth/callback")]
    public async Task<IActionResult> Callback([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest("Missing ?code");

        const string tokenEndpoint = "https://openapi.ctrader.com/apps/token";

        var app = new App(_opt.ClientId.Trim(), _opt.ClientSecret.Trim(), _opt.RedirectUri.Trim());

        try
        {
            var token = await TokenFactory.GetToken(code, app, tokenEndpoint);
            using (LogContext.PushProperty("ops", true))
                log.LogInformation("✅ Token OK, ExpiresIn={Exp}s", token.ExpiresIn);

            return Ok(token);
        }
        catch (HttpRequestException ex)
        {
            using (LogContext.PushProperty("ops", true))
                log.LogError(ex, "❌ Token exchange failed. client_id_len={Len}, redirect={Redirect}",
                    _opt.ClientId?.Trim().Length ?? 0, _opt.RedirectUri);

            return Problem(
                "Token exchange failed. Check ClientId/Secret/RedirectUri exactly match portal app settings.");
        }
    }


    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts([FromQuery] string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return BadRequest("missing accessToken");

        var profRes = await OpenApiCompat.SendAndWaitAsync(
            session.Client,
            "ProtoOAGetCtidProfileByTokenReq",
            "ProtoOAGetCtidProfileByTokenRes",
            "ProtoOaGetCtidProfileByTokenReq",
            init: req => req.GetType().GetProperty("AccessToken")!.SetValue(req, accessToken),
            timeout: TimeSpan.FromSeconds(5)
        );
        var ctid = OpenApiCompat.GetLong(profRes, "Ctid");

        try
        {
            var byTokenRes = await OpenApiCompat.SendAndWaitAsync(
                session.Client,
                "ProtoOAGetCtidProfileByTokenReq",
                "ProtoOAGetCtidProfileByTokenRes",
                "ProtoOaGetCtidProfileByTokenReq",
                init: req => req.GetType().GetProperty("AccessToken")!.SetValue(req, accessToken),
                timeout: TimeSpan.FromSeconds(5)
            );

            using (LogContext.PushProperty("ops", true))
                log.LogInformation("👤 Accounts (by AccessToken).");

            var accs = byTokenRes.GetType().GetProperty("CtidTraderAccount")?.GetValue(byTokenRes) ?? byTokenRes;
            return Ok(accs);
        }
        catch
        {
            // نادیده بگیر و می‌رویم سراغ مسیر بعدی
        }

        try
        {
            var byCtidRes = await OpenApiCompat.SendAndWaitAsync(
                session.Client,
                "ProtoOAGetAccountListByCtidReq",
                "ProtoOAGetAccountListByCtidRes",
                "ProtoOaGetAccountListByCtidReq",
                init: req => req.GetType().GetProperty("Ctid")!.SetValue(req, ctid)
            );

            using (LogContext.PushProperty("ops", true))
                log.LogInformation("👤 Accounts (by CTID {Ctid}).", ctid);

            var accs = byCtidRes.GetType().GetProperty("CtidTraderAccount")?.GetValue(byCtidRes) ?? byCtidRes;
            return Ok(accs);
        }
        catch (Exception ex)
        {
            using (LogContext.PushProperty("ops", true))
                log.LogError(ex, "⚠️ Neither 'ByAccessToken' nor 'ByCtid' account-list messages are available.");
            return Problem(
                "Your OpenAPI.Net package version doesn't expose account list messages I tried. Update the package or share the package version so I tailor exact names.");
        }
    }


    [HttpGet("symbols")]
    public async Task<IActionResult> Symbols([FromQuery] int take = 10)
    {
        var listTcs = new TaskCompletionSource<ProtoOASymbolsListRes>();
        using var listSub = session.Client
            .OfType<ProtoOASymbolsListRes>()
            .Where(r => r.CtidTraderAccountId == session.AccountId)
            .Subscribe(r => listTcs.TrySetResult(r));

        await session.Client.SendMessage(new ProtoOASymbolsListReq
        {
            CtidTraderAccountId = session.AccountId
        }, ProtoOAPayloadType.ProtoOaSymbolsListReq);

        var listRes = await listTcs.Task;

        var lights = listRes.Symbol.Take(Math.Max(1, take)).ToList();
        var ids = lights.Select(l => l.SymbolId).ToList();

        var byIdTcs = new TaskCompletionSource<ProtoOASymbolByIdRes>();
        using var byIdSub = session.Client
            .OfType<ProtoOASymbolByIdRes>()
            .Where(r => r.CtidTraderAccountId == session.AccountId)
            .Subscribe(r => byIdTcs.TrySetResult(r));

        var byIdReq = new ProtoOASymbolByIdReq {CtidTraderAccountId = session.AccountId};
        byIdReq.SymbolId.AddRange(ids); // repeated field

        await session.Client.SendMessage(byIdReq, ProtoOAPayloadType.ProtoOaSymbolByIdReq);

        var byIdRes = await byIdTcs.Task;

        var map = (from s in byIdRes.Symbol
            join l in lights on s.SymbolId equals l.SymbolId
            select new
            {
                s.SymbolId,
                l.SymbolName,
                s.Digits,
                s.LotSize,
                s.MinVolume,
                s.StepVolume
            }).ToList();

        using (LogContext.PushProperty("ops", true))
            log.LogInformation("📦 Symbols fetched: {Count}", map.Count);

        return Ok(map);
    }

    [HttpGet("debug/options")]
    public IActionResult DebugOptions()
    {
        return Ok(new
        {
            Mode = _opt.Mode,
            ClientId = string.IsNullOrWhiteSpace(_opt.ClientId) ? "(empty)" : $"len={_opt.ClientId.Trim().Length}",
            ClientSecret = string.IsNullOrWhiteSpace(_opt.ClientSecret)
                ? "(empty)"
                : $"len={_opt.ClientSecret.Trim().Length}",
            RedirectUri = _opt.RedirectUri,
            AccountId = _opt.AccountId
        });
    }


    [HttpGet("auth/url")]
    public IActionResult AuthUrl([FromQuery] string scope = "trading")
    {
        var s = scope.Equals("account", StringComparison.OrdinalIgnoreCase) ? "accounts" : "trading";
        var url = "https://id.ctrader.com/my/settings/openapi/grantingaccess/"
                  + $"?client_id={Uri.EscapeDataString(_opt.ClientId.Trim())}"
                  + $"&redirect_uri={Uri.EscapeDataString(_opt.RedirectUri.Trim())}"
                  + $"&scope={s}&product=web";
        return Ok(url);
    }
}