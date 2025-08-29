using Application.Command;
using Domain;
using Domain.Enum;
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;
using MediatR;

namespace Application.Handler;

public sealed class EvalExecHandler : IRequestHandler<EvalExecCommand, EvalExecResult>
{
    private readonly StrategyEngine _engine;
    private readonly DailyTradePolicy _gate;
    private readonly ISeriesStore _series;
    private readonly IOrderExecutionPort _exec;

    public EvalExecHandler(StrategyEngine engine, DailyTradePolicy gate, ISeriesStore series, IOrderExecutionPort exec)
    { _engine = engine; _gate = gate; _series = series; _exec = exec; }

    public async Task<EvalExecResult> Handle(EvalExecCommand r, CancellationToken ct)
    {
        var decision = _engine.Evaluate(new Symbol(r.Symbol), new Timeframe(r.Timeframe),
            r.Bid, r.Ask, new Atr(r.Atr), r.Close, _gate, _series);

        if (decision.Action == ActionKind.Hold)
            return new EvalExecResult("NoTrade", null, decision.Note);

        var req = new TradeRequest(new Symbol(r.Symbol), decision.Action,
            decision.Size.Value, decision.Sl ?? 0, decision.Tp ?? 0, "EvalExec");

        var execRes = await _exec.OpenMarketAsync(req, ct);
        if (!execRes.IsSuccess) return new EvalExecResult("Failed", null, execRes.Error ?? "exec failed");

        // ✅ الان شمارش روزانه فقط وقتی موفق شد زیاد می‌شود
        _gate.RegisterTrade();

        return new EvalExecResult("Executed", execRes.OrderId?.Value, decision.Note);
    }
}