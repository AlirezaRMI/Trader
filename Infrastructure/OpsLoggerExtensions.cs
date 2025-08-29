using Microsoft.Extensions.Logging;

namespace Infrastructure;

public static class OpsLoggerExtensions
{
    public static ILogger Op(this ILogger logger)
        => logger;
}