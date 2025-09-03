namespace Domain.Enum;

public record SymbolInfo
{
    public string SymbolName { get; init; }
    public double PipSize { get; init; }     
    public double StepVolume { get; init; }  
    public int Digits { get; init; }      
    public long LotSize { get; init; }  
}