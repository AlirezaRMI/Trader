namespace Domain.Enum;

public record AccountInfo
{
    public long AccountId { get; init; }
    public double Equity { get; init; }
    public double Balance { get; init; }
    public string BrokerName { get; init; } = "";
    public string Server { get; init; } = "";
    public string TerminalSession { get; init; } = "";
    public string Currency { get; init; } = "USD";
    public bool IsDemo { get; init; }
    public double FreeMargin { get; init; }
}
