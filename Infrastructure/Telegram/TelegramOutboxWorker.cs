using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Telegram;

public sealed record NotificationStatus(bool Configured, bool DeliveryEnabled, string? LastError, int Pending);

public sealed class TelegramOutboxWorker(
    SqliteTradeStore store,
    IConfiguration configuration,
    ILogger<TelegramOutboxWorker> logger) : BackgroundService
{
    private readonly HttpClient _http = new(new HttpClientHandler {AllowAutoRedirect = false})
        {Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 64 * 1024};
    private readonly Dictionary<string, DateTimeOffset> _recipientNextSend = new(StringComparer.Ordinal);
    private DateTimeOffset _nextSend;
    private NotificationStatus _status = new(false, false, null, 0);
    public NotificationStatus Status => Volatile.Read(ref _status);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = configuration["Telegram:BotToken"]?.Trim();
        var recipients = configuration.GetSection("Telegram:ChatIds").Get<string[]>()
            ?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var enabled = configuration.GetValue("Telegram:DeliveryEnabled", true);
        var configured = !string.IsNullOrWhiteSpace(token) &&
                         token.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-') && recipients.Length > 0;
        // Persist a channel-wide cooldown without storing the bot token itself.
        var channel = "telegram:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));
        var retryAt = store.NotificationChannelRetryAt(channel);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                var deliveryEnabled = enabled && store.LoadSettings().TelegramEnabled;
                Publish(_status with {Configured = configured, DeliveryEnabled = deliveryEnabled,
                    Pending = store.NotificationPendingCount()});
                if (!configured || !deliveryEnabled || DateTimeOffset.UtcNow < retryAt) continue;
                foreach (var item in store.PendingNotifications())
                {
                    DeliveryResult? failure = null;
                    foreach (var recipient in recipients)
                    {
                        if (store.RecipientDelivered(item.Id, recipient)) continue;
                        // Pausing delivery never changes order execution or removes unsent notifications.
                        if (!store.LoadSettings().TelegramEnabled) break;
                        var result = await SendAsync(token!, recipient, item.Text, item.Attempts, stoppingToken);
                        if (result.Accepted)
                        {
                            store.MarkRecipientDelivered(item.Id, recipient);
                            continue;
                        }

                        if (failure is null || result.GlobalPause || result.RetryDelay > failure.RetryDelay)
                            failure = result;
                        if (result.GlobalPause) break;
                    }

                    if (failure is not null)
                    {
                        var nextAttempt = DateTimeOffset.UtcNow + failure.RetryDelay;
                        if (failure.GlobalPause) store.DeferNotificationChannel(channel, nextAttempt);
                        store.DeferNotification(item.Id, failure.Error!, nextAttempt);
                        if (_status.LastError != failure.Error)
                        {
                            store.AppendJournal("Warning", "TelegramDeliveryFailed", failure.Error!);
                            // Never log an exception, request URL, raw response, token, recipient or message body.
                            logger.LogWarning("Telegram: {Reason}", failure.Error);
                        }
                        Publish(_status with {LastError = failure.Error, Pending = store.NotificationPendingCount()});
                        if (failure.GlobalPause)
                        {
                            retryAt = nextAttempt;
                            break;
                        }
                    }
                    else if (recipients.All(recipient => store.RecipientDelivered(item.Id, recipient)))
                    {
                        store.MarkNotification(item.Id, true, null);
                        Publish(_status with {Pending = store.NotificationPendingCount()});
                    }

                    if (!store.LoadSettings().TelegramEnabled) break;
                }

                if (store.NotificationPendingCount() == 0) Publish(_status with {LastError = null, Pending = 0});
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<DeliveryResult> SendAsync(string token, string recipient, string text, int attempts,
        CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts + 1L, 6))));
        try
        {
            var earliest = _recipientNextSend.TryGetValue(recipient, out var next) && next > _nextSend ? next : _nextSend;
            var delay = earliest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            _nextSend = now.AddSeconds(1.1);
            _recipientNextSend[recipient] = now.AddSeconds(3.1); // Conservative spacing; server cooldown takes priority.
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = recipient, ["text"] = text, ["parse_mode"] = "HTML",
                ["link_preview_options"] = "{\"is_disabled\":true}"
            });
            using var response = await _http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", body,
                cancellationToken);
            JsonDocument? document = null;
            try
            {
                document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
            }
            catch (JsonException) { /* HTTP status/Retry-After still apply to a malformed error body. */ }
            using var json = document;
            var root = json?.RootElement ?? default;
            var isObject = root.ValueKind == JsonValueKind.Object;
            if (response.IsSuccessStatusCode && isObject && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("result", out var message) && message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("message_id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var messageId) && messageId > 0)
                return new(true, null, TimeSpan.Zero, false);

            var code = isObject && root.TryGetProperty("error_code", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var errorCode)
                ? errorCode : (int)response.StatusCode;
            var parameters = isObject && root.TryGetProperty("parameters", out var details) && details.ValueKind == JsonValueKind.Object
                ? details : default;
            if (code == 429 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var seconds = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("retry_after", out var retry)
                              && retry.ValueKind == JsonValueKind.Number && retry.TryGetInt32(out var duration) ? Math.Max(1, duration) : 0;
                var header = response.Headers.RetryAfter;
                var headerDelay = header?.Delta ?? (header?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.Zero;
                var serverDelay = TimeSpan.FromSeconds(seconds);
                var wait = serverDelay > headerDelay ? serverDelay : headerDelay;
                return Failed("محدودیت ارسال تلگرام؛ پیام محفوظ است و پس از زمان مجاز دوباره ارسال می‌شود.",
                    wait > TimeSpan.Zero ? wait : backoff, true);
            }

            if (code == 401)
                return Failed("توکن تلگرام پذیرفته نشد؛ Bot Token را اصلاح و برنامه را دوباره اجرا کن.", TimeSpan.FromMinutes(5), true);
            if (code == 403)
                return Failed("ربات اجازهٔ پیام‌دادن ندارد؛ در چت ربات Start بزن و مسدودبودن یا دسترسی گروه را بررسی کن.",
                    TimeSpan.FromMinutes(5), false);
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("migrate_to_chat_id", out _))
                return Failed("گروه تلگرام منتقل شده است؛ Chat ID جدید را در تنظیمات وارد کن.", TimeSpan.FromMinutes(5), false);
            if (code == 400)
                return Failed("تلگرام پیام را نپذیرفت؛ Chat ID، عضویت ربات و قالب پیام را بررسی کن. پیام حذف نشده است.",
                    TimeSpan.FromMinutes(5), false);
            return Failed("ارسال تلگرام فعلاً ممکن نیست؛ پیام برای تلاش مجدد نگه داشته شد.", backoff, true);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed("ارتباط یا پاسخ تلگرام ناموفق بود؛ پیام برای تلاش مجدد نگه داشته شد.", backoff, true);
        }
    }

    private void Publish(NotificationStatus status) => Volatile.Write(ref _status, status);
    private static DeliveryResult Failed(string error, TimeSpan retry, bool global) => new(false, error, retry, global);
    private sealed record DeliveryResult(bool Accepted, string? Error, TimeSpan RetryDelay, bool GlobalPause);

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
