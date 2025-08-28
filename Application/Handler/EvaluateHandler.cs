using Application.Command;
using Domain.Polisy;
using Domain.Services;
using Domain.Trading;
using MediatR;

namespace Application.Handler;

public sealed class EvaluateHandler(StrategyEngine engine, DailyTradePolicy gate, ISeriesStore series)
    : IRequestHandler<EvalExecCommand, DecisionDto>
{
    public Task<DecisionDto> Handle(EvalExecCommand r, CancellationToken ct)
    {
        var dec = engine.Evaluate(new Symbol(r.Symbol), new Timeframe(r.Timeframe),
            r.Bid, r.Ask, new Atr(r.Atr), r.Close, gate, series);

        var dto = new DecisionDto(dec.Action.ToString(), dec.Size.Value,
            dec.Sl ?? 0, dec.Tp ?? 0, dec.Note);
        return Task.FromResult(dto);
    }
}