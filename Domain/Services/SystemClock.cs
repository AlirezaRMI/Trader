namespace Domain.Services;

public class SystemClock : IClock
{
    public DateTime UtcNow  => DateTime.UtcNow;
}