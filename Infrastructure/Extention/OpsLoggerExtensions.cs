using Microsoft.Extensions.Logging;

namespace Infrastructure.Extention;

public static class OpsLoggerExtensions
{
    public static ILogger Op(this ILogger logger)
        => logger;
}