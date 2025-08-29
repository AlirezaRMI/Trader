// Trader/Application/UseCase/EvalExecHandler.cs

using Application.Command;
using Domain;
using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;
using MediatR;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Application.Handler;

public sealed class EvalExecHandler(StrategyEngine engine, DailyTradePolicy gate, ISeriesStore series, IOrderExecutionPort exec,
    ILogger<EvalExecHandler> log) : IRequestHandler<EvalExecCommand, EvalExecResult>
{


    public async Task<EvalExecResult> Handle(EvalExecCommand r, CancellationToken ct)
    {
        var decision = engine.Evaluate(new Symbol(r.Symbol), new Timeframe(r.Timeframe),
            r.Bid, r.Ask, new Atr(r.Atr), r.Close, gate, series);

        using (LogContext.PushProperty("ops", true))
        {
            if (decision.Action == ActionKind.Hold)
            {
                log.LogInformation("⏸️ HOLD: {Note}", decision.Note);
                return new EvalExecResult("NoTrade", null, decision.Note);
            }

            log.LogInformation("🧮 DECISION {Action} Size={Size} SL={SL} TP={TP}",
                decision.Action, decision.Size.Value, decision.Sl, decision.Tp);

            var req = new TradeRequest(new Symbol(r.Symbol), decision.Action,
                decision.Size.Value, decision.Sl ?? 0, decision.Tp ?? 0, "EvalExec");

            var execRes = await exec.OpenMarketAsync(req, ct);
            if (!execRes.IsSuccess)
            {
                log.LogError("❌ EXEC FAILED: {Error}", execRes.Error ?? "exec failed");
                return new EvalExecResult("Failed", null, execRes.Error ?? "exec failed");
            }

            gate.RegisterTrade();

            log.LogInformation("🚀 EXECUTED {Side} {Lots} {Symbol} @OrderId={OrderId}",
                decision.Action, decision.Size.Value, r.Symbol, execRes.OrderId?.Value);

            return new EvalExecResult("Executed", execRes.OrderId?.Value, decision.Note);
        }
    }
}
