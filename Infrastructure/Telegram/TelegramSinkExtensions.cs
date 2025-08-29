using System.Net;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Telegram.Bot;

namespace Infrastructure.Telegram;

public static class TelegramBotSinkExtensions
{
    [Obsolete("Obsolete")]
    public static LoggerConfiguration TelegramBot(
        this LoggerSinkConfiguration sinkConfiguration,
        string botToken,
        string chatId,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Information,
        int batchSizeLimit = 20,
        int periodSeconds = 2,
        int queueLimit = 2000,
        bool disableNotification = true,
        string? proxyUrl = null,
        string? proxyUser = null,
        string? proxyPass = null)
    {
        ITelegramBotClient botClient;
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxyUrl)
                {
                    Credentials = (!string.IsNullOrWhiteSpace(proxyUser) && proxyPass != null)
                        ? new NetworkCredential(proxyUser, proxyPass)
                        : null
                },
                UseProxy = true
            };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            botClient = new TelegramBotClient(botToken, http);
        }
        else
        {
            botClient = new TelegramBotClient(botToken);
        }

        var sink = new TelegramBotSink(
            botClient,
            chatId,
            batchSizeLimit: batchSizeLimit,
            period: TimeSpan.FromSeconds(periodSeconds),
            queueLimit: queueLimit,
            disableNotification: disableNotification);

        return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
    }
}