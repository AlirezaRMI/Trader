namespace Domain.Enum;

public record AccountInfo
{
    public long AccountId { get; init; }
    public double Equity { get; init; } 
    public double Balance { get; init; }
    public string BrokerName { get; init; }
}