using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Infrastructure;


public sealed class TelegramSink(
    string botToken,
    string chatId,
    ILogger<TelegramSink> logger,
    IFormatProvider? formatProvider = null)
    : ILogEventSink
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public void Emit(LogEvent logEvent)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("HH:mm:ss")).Append("] ");
            sb.Append(logEvent.Level).Append(' ');
            if (logEvent.Properties.TryGetValue("SourceContext", out var src))
                sb.Append(src.ToString().Trim('"')).Append(' ');
            sb.Append(logEvent.RenderMessage(formatProvider));

            if (logEvent.Exception != null)
            {
                sb.AppendLine().Append("EX: ").Append(logEvent.Exception);
            }

            var payload = new
            {
                chat_id = chatId,
                text = sb.ToString(),
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            _ = Http.PostAsync(url, content);
        }
        catch
        {
            logger.LogInformation("prospecting end in catch...");
        }
    }
}