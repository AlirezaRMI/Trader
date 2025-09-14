namespace Domain.Enum;

public class EconomicEvent
{
    public DateTime EventTime { get; set; }
    public string? Currency { get; set; }
    public string? Impact { get; set; } // "High", "Medium", "Low"
    public string? EventName { get; set; }
}

public class EodhdEventItem
{
    public DateTime Date { get; set; }
    public string? Currency { get; set; }
    public string? Impact { get; set; }
    public string? Event { get; set; }
}