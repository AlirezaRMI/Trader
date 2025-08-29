using Domain.Polisy;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/admin")]
[Tags("Admin")]
public sealed class AdminController : ControllerBase
{
    [HttpPost("policy/reset")]
    public IActionResult Reset([FromServices] DailyTradePolicy gate)
    {
        gate.Reset();  // متد Reset را پایین اضافه می‌کنیم
        return Ok(new { ok = true, reset = true });
    }
}