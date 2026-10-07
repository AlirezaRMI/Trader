namespace Domain.Enum;

public class SymbolDetails
{
    public string SymbolName { get; set; } = "";
    public double LotSize { get; set; }
    public string AccountCurrency { get; set; } = "";
    public string QuoteCurrency { get; set; } = "";
    public double PipSize { get; set; }
    public double StepVolume { get; set; }
    public double QuoteToAccountRate { get; set; }
    public double TickSize { get; set; }
    public double TickValue { get; set; }
    public double MinVolume { get; set; }
    public double MaxVolume { get; set; } = 100;
    public double Point { get; set; }
    public int Digits { get; set; } = 5;
    public int StopsLevel { get; set; }
    public int FreezeLevel { get; set; }
}
