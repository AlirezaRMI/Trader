using Domain.Services;

namespace Domain.Polisy;

public class DailyTradePolicy
{
    private readonly IClock _clock;
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");
    private DateOnly _day; private int _count;

    public DailyTradePolicy(IClock clock)
    {
        _clock = clock;
        _day = Today();
        _count = 0;
    }
    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _tz));

    public bool CanTrade()
    {
        var t = Today();
        if (t != _day) { _day = t; _count = 0; }
        return _count < 5;
    }
    public void RegisterTrade() => _count++;
    public int Count => _count;
    public void Reset() { _day = Today(); _count = 0; }
}