namespace Domain.Services;

public interface IClock
{
    DateTime UtcNow { get; }
}