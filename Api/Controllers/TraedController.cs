using Application.Command;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/trade")]
[Tags("Trade")]
public sealed class TradeController(
    IMediator mediator,
    IValidator<EvaluateCommand> evalV,
    IValidator<EvalExecCommand> execV)
    : ControllerBase
{
    [HttpPost("evaluate")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Evaluate([FromBody] EvaluateCommand cmd)
    {
        var res = await evalV.ValidateAsync(cmd);
        var decision = await mediator.Send(cmd); // DecisionDto
        var csv = $"{decision.Action},{decision.Size},{decision.Sl},{decision.Tp},{decision.Note.Replace(',', ' ')}";
        return Content(csv, "text/plain");
    }

    // تحلیل + اجرا: JSON
    [HttpPost("eval-exec")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(EvalExecResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EvalExec([FromBody] EvalExecCommand cmd)
    {
        var res = await execV.ValidateAsync(cmd);
        var result = await mediator.Send(cmd);
        return Ok(result);
    }
}