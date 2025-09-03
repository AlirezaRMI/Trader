using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("Handling {RequestName} {@Request}", name, request);
        try
        {
            var response = await next(ct);
            log.LogInformation("Handled {RequestName} => {@Response}", name, response);
            return response;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error in {RequestName} {@Request}", name, request);
            throw;
        }
    }
}