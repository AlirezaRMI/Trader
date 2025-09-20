namespace Domain.Enum;

public class SymbolDetails
{
    public string SymbolName { get; set; }
    public double LotSize { get; set; }
    public string AccountCurrency { get; set; }
    public string QuoteCurrency { get; set; }
    public double PipSize { get; set; }
    public double StepVolume { get; set; }
    public double QuoteToAccountRate { get; set; }
}