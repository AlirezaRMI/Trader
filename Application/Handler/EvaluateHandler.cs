// Trader/Application/Validation/EvaluateHandler.cs

using Application.Command;
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;
using MediatR;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Application.Handler;

public sealed class EvaluateHandler(
    StrategyEngine engine,
    DailyTradePolicy gate,
    ISeriesStore series,
    ILogger<EvaluateHandler> log)
    : IRequestHandler<EvaluateCommand, DecisionDto>
{
    public Task<DecisionDto> Handle(EvaluateCommand r, CancellationToken ct)
    {
        var decision = engine.Evaluate(new Symbol(r.Symbol), new Timeframe(r.Timeframe),
            r.Bid, r.Ask, new Atr(r.Atr), r.Close, gate, series);

        using (LogContext.PushProperty("ops", true))
        {
            log.LogInformation("📊 EVALUATE {Symbol}/{TF} -> {Action} Size={Size} SL={SL} TP={TP}",
                r.Symbol, r.Timeframe, decision.Action, decision.Size.Value, decision.Sl, decision.Tp);
        }

        return Task.FromResult(new DecisionDto(
            decision.Action.ToString(),
            decision.Size.Value,
            decision.Sl ?? 0,
            decision.Tp ?? 0,
            decision.Note
        ));
    }
}