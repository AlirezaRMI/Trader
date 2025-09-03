namespace Domain.Enum;

public record MarketData
{
    public double Open { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public double Close { get; init; }
    public double Atr { get; init; }         
    public double Bid { get; init; }         
    public double Ask { get; init; }         
}