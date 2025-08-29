using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var resp = await next(ct);
        sw.Stop();

        if (sw.ElapsedMilliseconds > 200)
            log.LogWarning("SLOW {Request} took {Elapsed} ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);
        else
            log.LogDebug("{Request} took {Elapsed} ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return resp;
    }
}