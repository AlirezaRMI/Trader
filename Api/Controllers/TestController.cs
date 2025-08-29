using Domain.Services;
using Domain.Trading;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/test")]
[Tags("Test")]
public sealed class TestController : ControllerBase
{
    public sealed record SeedReq(string Symbol, string Timeframe, int Count = 30, double Start = 1.1000, double Step = 0.0003);

    private readonly ISeriesStore _series;
    public TestController(ISeriesStore series) { _series = series; }

    [HttpPost("seed/buy")]
    public IActionResult SeedBuy([FromBody] SeedReq req)
    {
        var s = new Symbol(req.Symbol);
        var tf = new Timeframe(req.Timeframe);
        for (int i = 0; i < req.Count; i++)
            _series.AppendClose(s, tf, req.Start + req.Step * i);   // صعودی
        return Ok(new { mode = "buy", req.Symbol, req.Timeframe, req.Count });
    }

    [HttpPost("seed/sell")]
    public IActionResult SeedSell([FromBody] SeedReq req)
    {
        var s = new Symbol(req.Symbol);
        var tf = new Timeframe(req.Timeframe);
        for (int i = 0; i < req.Count; i++)
            _series.AppendClose(s, tf, req.Start - req.Step * i);   // نزولی
        return Ok(new { mode = "sell", req.Symbol, req.Timeframe, req.Count });
    }

    [HttpPost("seed/hold")]
    public IActionResult SeedHold([FromBody] SeedReq req)
    {
        var s = new Symbol(req.Symbol);
        var tf = new Timeframe(req.Timeframe);
        for (int i = 0; i < req.Count; i++)
        {
            var close = req.Start + (i % 2 == 0 ? +req.Step * 0.2 : -req.Step * 0.2); // رنج کم
            _series.AppendClose(s, tf, close);
        }
        return Ok(new { mode = "hold", req.Symbol, req.Timeframe, req.Count });
    }
}