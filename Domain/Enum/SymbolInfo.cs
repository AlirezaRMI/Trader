namespace Domain.Enum;

public record SymbolInfo
{
    public string SymbolName { get; init; } = "GBPUSD";
    public double PipSize { get; init; }
    public double StepVolume { get; init; }
    public int Digits { get; init; }
    public long LotSize { get; init; }
}