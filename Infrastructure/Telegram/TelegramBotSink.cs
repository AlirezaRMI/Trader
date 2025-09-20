using System.Globalization;
using System.Text;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Infrastructure.Telegram;

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
    private static readonly Random Rnd = new();

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

    private static string Format(LogEvent e)
    {
        var eventType = GetProperty(e, "EventType");

        switch (eventType)
        {
            case "TradeOpened":
            {
                var side = GetProperty(e, "Side") == "Buy" ? "خرید" : "فروش";
                var lots = GetProperty(e, "Lots");
                var symbol = GetProperty(e, "Symbol");
                var ticket = GetProperty(e, "Ticket");
             
                var entryPrice = GetProperty(e, "EntryPrice");
                var stopLoss = GetProperty(e, "StopLoss");
                var takeProfit = GetProperty(e, "TakeProfit");
               
                var templates = new[]
                {
                    $"🤣📈 یه معامله باز شد!\n\n" +
                    $"🍟 نوع: {side}\n" +
                    $"🥤 حجم: {lots} لات\n" +
                    $"🍕 نماد: {symbol}\n" +
                    $"🎯 نقطه ورود: {entryPrice}\n" +
                    $"🛡️ حد ضرر: {stopLoss}\n" +
                    $"💰 حد سود: {takeProfit}\n" +
                    $"🎫 تیکت: {ticket}\n\n" +
                    $"گوه تو روح بواش که ضرر کنه!",
                    $"💥🕶️ هوو! معامله زدیم!\n\n" +
                    $"📊 نوع: {side}\n" +
                    $"📦 حجم: {lots} لات\n" +
                    $"💹 نماد: {symbol}\n" +
                    $"🎯 ورود: {entryPrice}\n" +
                    $"🛡️ استاپ: {stopLoss}\n" +
                    $"💰 تارگت: {takeProfit}\n" +
                    $"🎫 تیکت: {ticket}\n\n" +
                    $"بکن توش لامصب!!! 😎",
                    $"🤬 معامله باز شد!\n\n" +
                    $"📌 نوع: {side}\n" +
                    $"⚖️ حجم: {lots} لات\n" +
                    $"💲 نماد: {symbol}\n" +
                    $"🎯 ورود: {entryPrice}\n" +
                    $"🛡️ ضرر: {stopLoss}\n" +
                    $"💰 سود: {takeProfit}\n" +
                    $"🎫 تیکت: {ticket}\n\n" +
                    $"ربات هٍرٍلی میکشد و بازار میگروسد!",
                    $"🖕 معامله جدید ثبت شد.\n\n" +
                    $"🔸 نوع: {side}\n" +
                    $"🔸 حجم: {lots} لات\n" +
                    $"🔸 نماد: {symbol}\n" +
                    $"🔸 ورود: {entryPrice}\n" +
                    $"🔸 استاپ: {stopLoss}\n" +
                    $"🔸 تارگت: {takeProfit}\n" +
                    $"🔸 تیکت: {ticket}\n\n" +
                    $"توشی لای لای ... بریم بزاریم به بازار 😡",
                    $"👹 معامله باز شد!\n\n" +
                    $"🔪 نوع: {side}\n" +
                    $"💣 حجم: {lots} لات\n" +
                    $"☠️ نماد: {symbol}\n" +
                    $"🎯 ورود: {entryPrice}\n" +
                    $"🛡️ استاپ: {stopLoss}\n" +
                    $"💰 تارگت: {takeProfit}\n" +
                    $"🎫 تیکت: {ticket}\n\n" +
                    $"شلوار های خود را در بیارید و خود را بکلاشنید!!! 🤬",
                };
                return templates[Rnd.Next(templates.Length)];
            }

            case "TradeClosed":
            {
                var closedTicket = GetProperty(e, "Ticket");
                
                // ==========================================================
                // ===== روش جدید، ساده و نهایی برای خواندن مقدار سود =====
                // ==========================================================
                var profitString = GetProperty(e, "Profit");
                Serilog.Debugging.SelfLog.WriteLine($"DEBUG: Received raw profit string for ticket {closedTicket}: '{profitString}'");

                double.TryParse(profitString, NumberStyles.Any, CultureInfo.InvariantCulture, out var profit);

                Serilog.Debugging.SelfLog.WriteLine($"DEBUG: Parsed profit value: {profit}");
                // ==========================================================

                if (profit >= 0)
                {
                    var templates = new[]
                    {
                        $"🤣💸 معامله با سود بسته شد!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"💰 سود: +{profit:F2}$\n\n" +
                        $"یا بابابابابابا بلش بره جااااا!!!!",
                        $"💰😎 معامله با موفقیت بسته شد.\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"🤑 سود خالص: +{profit:F2}$\n\n" +
                        $"نخوری به حق علی...بخور بگو بابام بزرگم کرد! 😎",
                        $"🤬 سود گرفتیم!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"💲 سود: +{profit:F2}$\n\n" +
                        $"بخوووووور دیوووووث بخووووور کونکش",
                        $"🖕 معامله بسته شد.\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"🤑 سود: +{profit:F2}$\n\n" +
                        $"بازار کیرته اوستا...اومیی ری حال؟!",
                        $"👹 سود رو کندم از گوشت تنت!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"💰 سود: +{profit:F2}$\n\n" +
                        $"پامو ببوس دیوث! 🤬",
                    };
                    return templates[Rnd.Next(templates.Length)];
                }
                else
                {
                    var templates = new[]
                    {
                        $"🤣💔 معامله با ضرر بسته شد!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"📉 ضرر: {profit:F2}$\n\n" +
                        $"کیرته کاکا...یکی دی! 🥤",
                        $"👊😤 ضرر کردیم.\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"💸 ضرر: {profit:F2}$\n\n" +
                        $"ای سر خر به لنگ اجدادش!!!",
                        $"🤬 ضرر خوردیم!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"📉 ضرر: {profit:F2}$\n\n" +
                        $"ای کیر توش... تلافی به شادی ایشالله",
                        $"🖕 ضرر شد!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"📉 مبلغ: {profit:F2}$\n\n" +
                        $"بازار مادرجنده! خو ضرر و کیرخر 🤬",
                        $"👹 از جیبم کندی!\n\n" +
                        $"🎫 تیکت: {closedTicket}\n" +
                        $"💸 ضرر: {profit:F2}$\n\n" +
                        $"حرومزاده خارتوگاییدم! این حسابو صاف می‌کنم باهات 😡",
                    };
                    return templates[Rnd.Next(templates.Length)];
                }
            }
            
            case "Error":
            {
                var templates = new[]
                {
                    $"🤣💥 خطا رخ داد!\n\n" +
                    $"🪲 پیام: {e.RenderMessage()}\n" +
                    $"📌 جزئیات: {e.Exception?.Message}\n\n" +
                    $"یکی کابلشو بکشه بزنه دوباره 🤣",
                    $"😡❌ ارور خوردیم!\n\n" +
                    $"🪲 پیام خطا: {e.RenderMessage()}\n" +
                    $"📌 جزئیات: {e.Exception?.Message}\n\n" +
                    $"وووو حالا درست ایبو صب کو😎",
                    $"🤬 خطای لعنتی!\n\n" +
                    $"🪲 خطا: {e.RenderMessage()}\n" +
                    $"📌 جزئیات: {e.Exception?.Message}\n\n" +
                    $"لعنت به این سیستم آشغال!",
                    $"🖕 ربات قاط زد!\n\n" +
                    $"🪲 مشکل: {e.RenderMessage()}\n" +
                    $"📌 جزئیات: {e.Exception?.Message}\n\n" +
                    $"یعنی بدبخت‌تر از این کُد پیدا نمیشه 🤬",
                    $"👹 سیستم ترکید!\n\n" +
                    $"🪲 خطا: {e.RenderMessage()}\n" +
                    $"📌 جزئیات: {e.Exception?.Message}\n\n" +
                    $"خاک تو سر این کد و سازنده‌ش 🤬",
                };
                return templates[Rnd.Next(templates.Length)];
            }

            default:
            {
                var templates = new[]
                {
                    $"🤣🖥️ آپدیت جدید:\n\n" +
                    $"📝 {e.RenderMessage()}\n\n" +
                    $"یعنی ربات هنوز زنده‌ست و قهوه‌شو خورده ☕",
                    $"📢😎 لاگ سیستم:\n\n" +
                    $"📝 {e.RenderMessage()}\n\n" +
                    $"ربات بیداره داشی!",
                    $"🤬 لاگ عمومی:\n\n" +
                    $"📝 {e.RenderMessage()}\n\n" +
                    $"هیچ نگران نبو...مشقی بی",
                };
                return templates[Rnd.Next(templates.Length)];
            }
        }
    }

    private static string GetProperty(LogEvent e, string name) =>
        e.Properties.TryGetValue(name, out var v) ? v.ToString().Trim('"') : "";

    private static ChatId ToChatId(string chatId) =>
        long.TryParse(chatId, out var id) ? new ChatId(id) : new ChatId(chatId);

    private async Task SendAsync(string text)
    {
        try
        {
            await _bot.SendMessage(
                chatId: ToChatId(_chatId),
                text: text,
                parseMode: ParseMode.Html,
                disableNotification: disableNotification);
        }
        catch (Exception ex)
        {
            Serilog.Debugging.SelfLog.WriteLine("Telegram sink send failed: {0}", ex);
        }
    }
}