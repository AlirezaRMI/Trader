using Application.Command;
using Domain;
using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;
using MediatR;

namespace Application.Handler;

public sealed class EvalExecHandler(
    StrategyEngine engine,
    DailyTradePolicy gate,
    ISeriesStore series,
    IOrderExecutionPort exec)
    : IRequestHandler<EvalExecCommand, EvalExecResult>
{
    public async Task<EvalExecResult> Handle(EvalExecCommand r, CancellationToken ct)
    {
        var decision = engine.Evaluate(
            new Symbol(r.Symbol),
            new Timeframe(r.Timeframe),
            r.Bid,
            r.Ask,
            new Atr(r.Atr),
            r.Close,
            gate,
            series
        );
        
        if (decision.Action == ActionKind.Hold)
            return new EvalExecResult("NoTrade", null, decision.Note);

        var lots = decision.Size.Value;
        var sl = decision.Sl ?? 0;
        var tp = decision.Tp ?? 0;

        var req = new TradeRequest(
            new Symbol(r.Symbol),
            decision.Action,
            lots,
            sl,
            tp,
            "EvalExec"
        );

        var execRes = await exec.OpenMarketAsync(req, ct);

        if (!execRes.IsSuccess)
            return new EvalExecResult("Failed", null, execRes.Error ?? "exec failed");

        return new EvalExecResult("Executed", execRes.OrderId?.Value, decision.Note);
    }
}