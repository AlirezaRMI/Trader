using Application.Broker;
using Application.Research;
using Domain.Enum;
using Infrastructure.Research;
using Infrastructure.Runtime;
using Trader.Protocol;
using System.Text.Json;

namespace Api;

public static class ResearchEndpoints
{
    public static void MapResearchEndpoints(this WebApplication app)
    {
        app.MapPost("/api/research/backtest", async (BacktestRequest request, ResearchRunner runner, HttpContext context) =>
        {
            try { return Results.Ok(await runner.RunAsync(token => new BacktestEngine().Run(request, token), context.RequestAborted)); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (OperationCanceledException) { return Results.Json(new { error = "Research run was cancelled or exceeded 30 seconds" }, statusCode: 408); }
        });
        app.MapPost("/api/research/optimize", async (OptimizationRequest request, ResearchRunner runner, HttpContext context) =>
        {
            try { return Results.Ok(await runner.RunAsync(token => new StrategyOptimizer().Run(request, token), context.RequestAborted)); }
            catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (OperationCanceledException) { return Results.Json(new { error = "Research run was cancelled or exceeded 30 seconds" }, statusCode: 408); }
        });
        app.MapGet("/api/research/market", async (int? count, TradingRuntime runtime, IBrokerGateway broker, HttpContext context) =>
        {
            var snapshot = runtime.Snapshot;
            var account = snapshot.Account;
            var analysis = snapshot.Analysis;
            if (account is null || analysis is null) return Results.Conflict(new { error = "A current broker analysis is required to import candles" });
            var limit = count ?? 3000;
            if (limit is < 100 or > 5000) return Results.BadRequest(new { error = "History count must be 100 to 5000" });
            try
            {
                var data = await BrokerContext.InvokeAsync(broker, account, "GET_HISTORY",
                    $"{analysis.Symbol},{snapshot.Settings.Strategy.EntryTimeframeMinutes},{limit}", context.RequestAborted);
                var candles = data.Deserialize<List<MarketData>>(WebSocketFrames.JsonOptions) ?? [];
                candles.Reverse();
                var parameters = snapshot.Settings.Strategy;
                var phaseCount = Math.Min(5000, parameters.PhaseHistoryBars +
                    (int)Math.Ceiling(limit * (double)parameters.EntryTimeframeMinutes / parameters.PhaseTimeframeMinutes) + 2);
                var patternCount = Math.Min(5000, parameters.HistoryBars +
                    (int)Math.Ceiling(limit * (double)parameters.EntryTimeframeMinutes / parameters.PatternTimeframeMinutes) + 2);
                var phaseData = await BrokerContext.InvokeAsync(broker, account, "GET_HISTORY",
                    $"{analysis.Symbol},{parameters.PhaseTimeframeMinutes},{phaseCount}", context.RequestAborted);
                var patternData = await BrokerContext.InvokeAsync(broker, account, "GET_HISTORY",
                    $"{analysis.Symbol},{parameters.PatternTimeframeMinutes},{patternCount}", context.RequestAborted);
                var phaseCandles = phaseData.Deserialize<List<MarketData>>(WebSocketFrames.JsonOptions) ?? [];
                var patternCandles = patternData.Deserialize<List<MarketData>>(WebSocketFrames.JsonOptions) ?? [];
                phaseCandles.Reverse(); patternCandles.Reverse();
                return Results.Ok(new BacktestRequest { Candles = candles, Symbol = analysis.Specification,
                    PhaseCandles = phaseCandles, PatternCandles = patternCandles,
                    Parameters = snapshot.Settings.Strategy, InitialEquity = account.Equity, RiskPercent = snapshot.Settings.RiskPercent,
                    MaximumRiskAmount = snapshot.Settings.MaximumRiskAmount, SpreadPoints = analysis.SpreadPoints,
                    DailyTradeLimit = snapshot.Settings.DailyTradeLimit, MaximumSpreadPoints = snapshot.Settings.MaximumSpreadPoints,
                    CommissionPerLot = parameters.CommissionPerLot, SlippagePoints = parameters.SlippageBufferPoints,
                    DailyDrawdownLimitPercent = snapshot.Settings.DailyDrawdownLimitPercent,
                    TotalDrawdownLimitPercent = snapshot.Settings.TotalDrawdownLimitPercent,
                    DrawdownCooldownHours = snapshot.Settings.DrawdownCooldownHours });
            }
            catch (BrokerException error) { return Results.Conflict(new { error = error.Message }); }
        });
    }
}
