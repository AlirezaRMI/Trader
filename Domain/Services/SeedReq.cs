namespace Domain.Services;

public sealed record SeedReq(
    string Symbol,
    string Timeframe,
    int    Count   = 25, 
    double Start   = 1.1000, 
    double Step    = 0.0003 
);