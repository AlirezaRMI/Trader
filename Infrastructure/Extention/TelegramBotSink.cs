using System.Text;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Infrastructure.Extention;

[Obsolete("Obsolete")]
public sealed class TelegramBotSink(
    ITelegramBotClient botClient,
    string chatId,
    int batchSizeLimit,
    TimeSpan period,
    int queueLimit = 2000,
    bool disableNotification = true)
    : PeriodicBatchingSink(batchSizeLimit, period, queueLimit)
{
    private readonly ITelegramBotClient _bot = botClient ?? throw new ArgumentNullException(nameof(botClient));
    private readonly string _chatId = chatId ?? throw new ArgumentNullException(nameof(chatId));

    private const int MaxText = 4000;

    protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
    {
        var sb = new StringBuilder();

        foreach (var e in events)
        {
            var line = Format(e);
            if (sb.Length + line.Length + 1 > MaxText)
            {
                await SendAsync(sb.ToString());
                sb.Clear();
            }
            sb.AppendLine(line);
        }

        if (sb.Length > 0)
            await SendAsync(sb.ToString());
    }

    private static string Emoji(LogEventLevel level) => level switch
    {
        LogEventLevel.Information => "ℹ️",
        LogEventLevel.Warning     => "⚠️",
        LogEventLevel.Error       => "❌",
        LogEventLevel.Fatal       => "💥",
        LogEventLevel.Debug       => "🐞",
        LogEventLevel.Verbose     => "🔍",
        _                         => "ℹ️"
    };

    private static string Format(LogEvent e)
    {
        var ts  = DateTimeOffset.Now.ToString("HH:mm:ss");
        var msg = e.RenderMessage();

        var side    = Pick("Side");
        var symbol  = Pick("Symbol");
        var lots    = Pick("Lots");
        var orderId = Pick("OrderId");

        var tail = (!string.IsNullOrEmpty(side) || !string.IsNullOrEmpty(symbol))
            ? $" • {side} {lots} {symbol} {(string.IsNullOrEmpty(orderId) ? "" : $"#{orderId}")}"
            : "";

        var text = $"{Emoji(e.Level)} [{ts}] {msg}{tail}";

        if (e.Exception != null)
            text += Environment.NewLine + "EX: " + e.Exception.Message;

        return text;

        string Pick(string name) => e.Properties.TryGetValue(name, out var v) ? v.ToString().Trim('"') : "";
    }

    private static ChatId ToChatId(string chatId)
        => long.TryParse(chatId, out var id) ? new ChatId(id) : new ChatId(chatId);

    private async Task SendAsync(string text)
    {
        try
        {
            await _bot.SendMessage(
                chatId: ToChatId(_chatId),
                text: text,
                disableNotification: disableNotification,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Serilog.Debugging.SelfLog.WriteLine("Telegram sink send failed: {0}", ex);
        }
    }
}
