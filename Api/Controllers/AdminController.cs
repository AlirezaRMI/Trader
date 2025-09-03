using Domain.Polisy;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Api.Controllers;

[ApiController]
[Route("api/admin")]
[Tags("Admin")]
public sealed class AdminController : ControllerBase
{
    // [HttpPost("policy/reset")]
    // public IActionResult Reset([FromServices] DailyTradePolicy gate)
    // {
    //     gate.Reset();
    //     return Ok(new {ok = true, reset = true});
    // }

    [HttpPost("telegram/test")]
    public async Task<IActionResult> TelegramTest([FromServices] IConfiguration cfg)
    {
        var token = cfg["Telegram:BotToken"];
        var chat = cfg["Telegram:ChatId"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat))
            return BadRequest("Token/ChatId not configured");

        var bot = new TelegramBotClient(token);
        var chatId = long.TryParse(chat, out var id) ? new ChatId(id) : new ChatId(chat);

        var resp = await bot.SendMessage(chatId,
            "Trader Api: Telegram test ✅ " + DateTime.Now.ToString("HH:mm:ss"));
        return Ok(new {resp.MessageId, resp.Chat.Id, resp.Date});
    }
}