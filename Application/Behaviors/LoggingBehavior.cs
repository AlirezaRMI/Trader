using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        log.LogInformation("Handling {RequestName}", name);
        try
        {
            var response = await next(ct);
            log.LogInformation("Handled {RequestName} in {ElapsedMs} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            log.LogInformation("Cancelled {RequestName} after {ElapsedMs} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            // Exception messages can also contain provider credentials; keep this boundary metadata-only.
            log.LogError("Failed {RequestName} after {ElapsedMs} ms ({ExceptionType})", name,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
