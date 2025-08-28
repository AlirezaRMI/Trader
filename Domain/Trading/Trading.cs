using Domain.Enum;

namespace Domain.Trading;

public readonly record struct Symbol(string Value);
public readonly record struct Timeframe(string Value);
public readonly record struct Price(double Value);
public readonly record struct Atr(double Value);
public readonly record struct Money(double Value);
public readonly record struct Lots(double Value);

public sealed record Candle(Symbol Symbol, Timeframe Tf, DateTime CloseTimeUtc,
    double Open, double High, double Low, double Close);



public sealed record Decision(ActionKind Action, Lots Size, double? Sl=null, double? Tp=null, string Note="");