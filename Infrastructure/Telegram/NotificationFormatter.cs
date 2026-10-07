using System.Globalization;
using System.Net;
using Application.Runtime;

namespace Infrastructure.Telegram;

public static class NotificationFormatter
{
    public static string Event(string title, string message, string? symbol = null) =>
        Header(title, EventIcon(title)) +
        (string.IsNullOrWhiteSpace(symbol) ? "" : $"\nنماد: <code>{Encode(symbol, 64)}</code>\n") +
        $"\n{Encode(message, 1000)}" + Footer();

    public static string Position(string title, BrokerPosition position, string currency)
    {
        var closed = position.CloseTime > 0;
        var side = position.Side?.ToUpperInvariant() switch
        {
            "BUY" => "خرید",
            "SELL" => "فروش",
            _ => "نامشخص"
        };
        var icon = !closed ? "✅" : !double.IsFinite(position.Profit) || position.Profit == 0 ? "⚪" :
            position.Profit > 0 ? "🟢" : "🔴";
        var profit = double.IsFinite(position.Profit)
            ? position.Profit.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "نامشخص";
        return Header(title, icon) +
               $"\n\n<b>{Encode(position.Symbol, 64)} · {side}</b>\n" +
               $"شناسه معامله: <code>{position.Ticket.ToString(CultureInfo.InvariantCulture)}</code>\n" +
               $"حجم: <code>{PositiveNumber(position.Lots)}</code> لات\n\n" +
               $"{(closed ? "قیمت ورود" : "قیمت مرجع سفارش")}: <code>{PositiveNumber(position.EntryPrice)}</code>\n" +
               $"🛡 حد ضرر: <code>{PositiveNumber(position.StopLoss, "ثبت نشده")}</code>\n" +
               $"🎯 حد سود: <code>{PositiveNumber(position.TakeProfit, "ثبت نشده")}</code>" +
               (closed
                   ? $"\n\n<b>سود/زیان خالص گزارش بروکر: {profit} {Encode(currency, 16)}</b>"
                   : "\n\n<i>قیمت مرجع و سطوح سفارش‌اند؛ قیمت پرشدن ممکن است متفاوت باشد.</i>") +
               Footer();
    }

    private static string Header(string title, string icon) => $"{icon} <b>{Encode(title, 80)}</b>\n━━━━━━━━━━━━━━";
    private static string Footer() => "\n\n<i>TRADER · زمان اعلان (UTC)</i>\n<code>" +
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "</code>";

    private static string EventIcon(string title) => title.Contains("نامشخص", StringComparison.Ordinal) ? "⚠️" :
        title.Contains("رد شد", StringComparison.Ordinal) ? "⛔" :
        title.Contains("متوقف", StringComparison.Ordinal) ? "⏸" :
        title.Contains("فعال", StringComparison.Ordinal) ? "▶️" :
        title.Contains("بازیابی", StringComparison.Ordinal) ? "🔎" : "🔔";

    private static string PositiveNumber(double value, string unavailable = "نامشخص") =>
        double.IsFinite(value) && value > 0 ? value.ToString("G10", CultureInfo.InvariantCulture) : unavailable;

    private static string Encode(string value, int maximum)
    {
        value ??= "";
        if (value.Length > maximum)
        {
            if (char.IsHighSurrogate(value[maximum - 1])) maximum--;
            value = value[..maximum] + "…";
        }

        return WebUtility.HtmlEncode(value);
    }
}
