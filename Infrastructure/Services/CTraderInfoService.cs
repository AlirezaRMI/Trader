// using Domain.Enum;
// using Domain.Services.Interfaces;
// using Infrastructure.Helpers;
// using Infrastructure.Maine;
// using Microsoft.Extensions.Logging;
// using OpenAPI.Net;
//
// namespace Infrastructure.Services;
// public sealed class CTraderInfoService(CTraderSession session, ILogger<CTraderInfoService> log) : ITraderInfoService
// {
//     private readonly OpenClient _client = session.Client;
//     private readonly long _accountId = session.AccountId;
//
//     public async Task<AccountInfo?> GetAccountInfoAsync()
//     {
//         try
//         {
//             var req = new ProtoOATraderReq { CtidTraderAccountId = _accountId };
//             log.LogInformation("Requesting trader info for AccountId {AccountId}...", _accountId);
//
//             var response = await _client.SendAndReceiveAsync<ProtoOATraderRes>(req, ProtoOAPayloadType.ProtoOaTraderReq);
//             
//             var accountData = response.Trader;
//
//             log.LogInformation("Successfully retrieved trader info for AccountId {AccountId}.", _accountId);
//             
//             return new AccountInfo
//             {
//                 AccountId = accountData.CtidTraderAccountId,
//                 //Equity = accountData.equity / 100.0,
//                 Balance = accountData.Balance / 100.0,
//                 BrokerName = accountData.BrokerName
//             };
//         }
//         catch (Exception ex)
//         {
//             log.LogError(ex, "Failed to retrieve account information for AccountId {AccountId}.", _accountId);
//             return null;
//         }
//     }
//     
//     public async Task<SymbolInfo?> GetSymbolInfoAsync(string symbolName)
//     {
//         try
//         {
//             log.LogInformation("Requesting symbols list for AccountId {AccountId} to find '{SymbolName}'...", _accountId, symbolName);
//             var listReq = new ProtoOASymbolsListReq { CtidTraderAccountId = _accountId };
//             var listResponse = await _client.SendAndReceiveAsync<ProtoOASymbolsListRes>(listReq, ProtoOAPayloadType.ProtoOaSymbolsListReq);
//             
//             var lightSymbol = listResponse.Symbol.FirstOrDefault(s => s.SymbolName.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
//             
//             if (lightSymbol == null)
//             {
//                 log.LogWarning("Symbol '{SymbolName}' not found in the symbols list for AccountId {AccountId}.", symbolName, _accountId);
//                 return null;
//             }
//
//             var symbolId = lightSymbol.SymbolId;
//             log.LogInformation("Found SymbolId {SymbolId} for '{SymbolName}'. Requesting details...", symbolId, symbolName);
//
//             var detailsReq = new ProtoOASymbolByIdReq { CtidTraderAccountId = _accountId };
//             detailsReq.SymbolId.Add(symbolId);
//             var detailsResponse = await _client.SendAndReceiveAsync<ProtoOASymbolByIdRes>(detailsReq, ProtoOAPayloadType.ProtoOaSymbolByIdReq);
//
//             var symbolData = detailsResponse.Symbol.FirstOrDefault();
//             
//             if (symbolData == null)
//             {
//                 log.LogWarning("Could not retrieve details for symbol ID {SymbolId} ({SymbolName}).", symbolId, symbolName);
//                 return null;
//             }
//             log.LogInformation("Successfully retrieved details for '{SymbolName}'.", symbolName);
//
//             // مپ کردن داده‌های دریافت شده به مدل دامنه
//             return new SymbolInfo
//             {
//                 SymbolName = symbolName,
//                 PipSize = 1 / Math.Pow(10, symbolData.Digits),
//                 StepVolume = symbolData.StepVolume,
//                 Digits = symbolData.Digits,
//                 LotSize = symbolData.LotSize
//             };
//         }
//         catch (Exception ex)
//         {
//             log.LogError(ex, "Failed to retrieve symbol info for {SymbolName} on AccountId {AccountId}.", symbolName, _accountId);
//             return null;
//         }
//     }
// }

using Domain.Enum;
using Infrastructure.Helpers;
using Infrastructure.Maine;
using Microsoft.Extensions.Logging;
using OpenAPI.Net;

namespace Infrastructure.Services;

public sealed class CTraderInfoService(CTraderSession session, ILogger<CTraderInfoService> log)
{
    private readonly OpenClient _client = session.Client;

    public async Task<AccountInfo?> GetAccountInfoAsync()
    {
        try
        {
            log.LogInformation("Sending a simple TEST request (ProtoOAVersionReq)...");

            var versionReq = new ProtoOAVersionReq();
            var versionRes = await _client.SendAndReceiveAsync<ProtoOAVersionRes>(versionReq, ProtoOAPayloadType.ProtoOaVersionReq);

            log.LogInformation("<<<< SUCCESS! Received a response from the server! Version: {Version} >>>>", versionRes.Version);

            return null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "The TEST request (ProtoOAVersionReq) also failed.");
            return null;
        }
        // ===== پایان کد تست =====
    }

    // متد زیر فعلاً استفاده نمی‌شود
    public Task<SymbolInfo?> GetSymbolInfoAsync(string symbolName)
    {
        return Task.FromResult<SymbolInfo?>(null);
    }
}