using MediatR;


namespace Application.Command;

public sealed record EvalExecCommand(
    string Symbol,
    string Timeframe,
    double Open,
    double High,
    double Low,
    double Close,
    double Atr,
    double Bid,
    double Ask,
    double Equity,
    int OpenTrades
) : IRequest<EvalExecResult>, IRequest<DecisionDto>;

public sealed record EvalExecResult(string Status, long? OrderId, string Note);