namespace Domain.Enum;

public record MarketData
{
    public long OpenTime { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Ask { get; set; }
    public double Bid { get; set; }
    public double EmaFast { get; set; }
    public double EmaSlow { get; set; } 
    public double Atr { get; set; }
    public double AtrSma { get; set; }
    public double Adx { get; set; }
    public int QuoteAgeSeconds { get; set; }
}
