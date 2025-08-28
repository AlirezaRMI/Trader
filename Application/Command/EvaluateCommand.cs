using MediatR;

namespace Application.Command;

public sealed record EvaluateCommand(
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
) : IRequest<DecisionDto>;